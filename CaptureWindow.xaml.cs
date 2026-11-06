using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace LocketWidget;

// hosts a short-lived webview2 that loads the site and records the feed request
// the site makes by itself. when the request is captured the window closes and
// the browser process goes away, so the ram is released.
public partial class CaptureWindow : Window
{
    readonly AppSettings _settings;
    readonly bool _silent;
    readonly TaskCompletionSource<CapturedRequest?> _done =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    bool _finished;
    bool _closed;

    public CaptureWindow(AppSettings settings, bool silent)
    {
        InitializeComponent();

        _settings = settings;
        _silent = silent;

        if (silent)
        {
            // webview2 needs a real window to live in, so park it off-screen
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -32000;
            Top = -32000;
            Width = 480;
            Height = 760;
            ShowInTaskbar = false;
            ShowActivated = false;
        }

        Loaded += async (_, _) => await StartAsync();

        Closed += (_, _) =>
        {
            _closed = true;
            _done.TrySetResult(null);
        };
    }

    // null = nothing captured (window closed by the user or timeout)
    public Task<CapturedRequest?> Result => _done.Task;

    async Task StartAsync()
    {
        try
        {
            // the profile folder keeps the login between runs
            var dataDir = Path.Combine(AppSettings.Dir, "webview");
            var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
            await web.EnsureCoreWebView2Async(env);

            var core = web.CoreWebView2;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Fetch);
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.XmlHttpRequest);
            core.WebResourceRequested += OnRequest;
            core.Navigate(_settings.SiteUrl);

            if (_silent)
            {
                _ = TimeoutAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Khong mo duoc WebView2: " + ex.Message);
            Close();
        }
    }

    async Task TimeoutAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(25));

        if (!_finished && !_closed)
        {
            Close();
        }
    }

    void OnRequest(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var request = e.Request;
        var url = request.Uri;

        // discovery mode: no marker yet, so just log the urls
        if (string.IsNullOrWhiteSpace(_settings.FeedUrlMarker))
        {
            var line = request.Method + " " + url + Environment.NewLine;
            File.AppendAllText(Path.Combine(AppSettings.Dir, "requests.log"), line);
            return;
        }

        if (_finished)
        {
            return;
        }

        if (request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!url.Contains(_settings.FeedUrlMarker, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // the request before login has no token: skip it
        if (!request.Headers.Contains("Authorization"))
        {
            return;
        }

        var captured = new CapturedRequest
        {
            Url = url,
            Method = request.Method,
        };

        foreach (var header in request.Headers)
        {
            captured.Headers[header.Key] = header.Value;
        }

        // reading the body may break this one page request, which is fine
        // because the window closes right after
        if (request.Content != null)
        {
            using var reader = new StreamReader(request.Content);
            captured.Body = reader.ReadToEnd();
        }

        _finished = true;
        SecureStore.Save(captured);
        _done.TrySetResult(captured);

        Dispatcher.BeginInvoke(() =>
        {
            if (!_closed)
            {
                Close();
            }
        });
    }
}
