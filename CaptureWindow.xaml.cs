using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;

namespace LocketWidget;

// hosts a short-lived webview2 that loads the site and records the feed request
// the site makes by itself. when the request is captured the window closes and
// the browser process goes away, so the ram is released.
public sealed partial class CaptureWindow : Window
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

        Title = "Locket Widget - đăng nhập";
        Native.ApplyIcon(this);

        var scale = Native.Scale(this);
        var width = (int)Math.Round(480 * scale);
        var height = (int)Math.Round(760 * scale);

        if (silent)
        {
            // webview2 needs a real window to live in, so park it off-screen
            AppWindow.IsShownInSwitchers = false;
            AppWindow.MoveAndResize(new RectInt32(-32000, -32000, width, height));
        }
        else
        {
            var area = DisplayArea.Primary.WorkArea;
            height = Math.Min(height, area.Height - 40);

            AppWindow.MoveAndResize(new RectInt32(
                area.X + (area.Width - width) / 2,
                area.Y + (area.Height - height) / 2,
                width,
                height));
        }

        Closed += (_, _) =>
        {
            _closed = true;

            try
            {
                Web.Close();
            }
            catch
            {
                // already gone
            }

            _done.TrySetResult(null);
        };

        // the webview can only start once the window is on screen
        Root.Loaded += async (_, _) => await StartAsync();
    }

    // null = nothing captured (window closed by the user or timeout)
    public Task<CapturedRequest?> Result => _done.Task;

    async Task StartAsync()
    {
        try
        {
            // the profile folder (WEBVIEW2_USER_DATA_FOLDER, set in App) keeps the login between runs
            await Web.EnsureCoreWebView2Async();

            var core = Web.CoreWebView2;
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
            Log.Write("WebView2: " + ex);
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

    void OnRequest(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs e)
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
            using var reader = new StreamReader(request.Content.AsStreamForRead());
            captured.Body = reader.ReadToEnd();
        }

        _finished = true;
        SecureStore.Save(captured);
        _done.TrySetResult(captured);

        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_closed)
            {
                Close();
            }
        });
    }
}
