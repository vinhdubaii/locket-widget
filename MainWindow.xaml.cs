using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace LocketWidget;

public partial class MainWindow : Window
{
    readonly AppSettings _settings = AppSettings.Load();
    readonly FeedClient _feed;
    readonly DispatcherTimer _timer = new();

    CapturedRequest? _session = SecureStore.Load();
    string? _lastUrl;
    bool _hasImage;
    bool _busy;

    public MainWindow()
    {
        InitializeComponent();

        _feed = new FeedClient(_settings);

        Width = _settings.Size;
        Height = _settings.Size;
        Topmost = _settings.AlwaysOnTop;
        topMenu.IsChecked = _settings.AlwaysOnTop;

        if (_settings.Left is double left && _settings.Top is double top)
        {
            Left = left;
            Top = top;
        }
        else
        {
            // first run: bottom-right corner of the work area
            var area = SystemParameters.WorkArea;
            Left = area.Right - Width - 24;
            Top = area.Bottom - Height - 24;
        }

        LoadCachedImage();

        _timer.Interval = TimeSpan.FromMinutes(Math.Max(1, _settings.RefreshMinutes));
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        Loaded += async (_, _) => await RefreshAsync();
        Closing += (_, _) => SavePosition();
    }

    async Task RefreshAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;

        try
        {
            if (string.IsNullOrWhiteSpace(_settings.FeedUrlMarker))
            {
                await DiscoverAsync();
                return;
            }

            if (_session == null && !await CaptureAsync(silent: false))
            {
                SetStatus("Chưa đăng nhập. Chuột phải > Đăng nhập lại.");
                return;
            }

            string? url;

            try
            {
                url = await _feed.GetLatestImageUrlAsync(_session!);
            }
            catch (SessionExpiredException)
            {
                // token expired: a hidden webview lets the site refresh it by itself,
                // and only if that fails we ask for a visible login
                var renewed = await CaptureAsync(silent: true) || await CaptureAsync(silent: false);
                if (!renewed)
                {
                    SetStatus("Phiên đăng nhập hết hạn.");
                    return;
                }

                url = await _feed.GetLatestImageUrlAsync(_session!);
            }

            if (url == null)
            {
                SetStatus("Không thấy URL ảnh trong feed. Kiểm tra ImageUrlMarker.");
                return;
            }

            if (url == _lastUrl)
            {
                return;
            }

            var bytes = await _feed.DownloadImageAsync(url);
            ShowImage(bytes);
            _lastUrl = url;
            card.ToolTip = "Cập nhật lúc " + DateTime.Now.ToString("HH:mm");
        }
        catch (NotSupportedException)
        {
            SetStatus("Windows chưa giải mã được WebP. Cài 'WebP Image Extensions' từ Microsoft Store.");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    // first run helper: log in, let the feed load, then pick the feed url from requests.log
    async Task DiscoverAsync()
    {
        SetStatus("Đăng nhập, đợi feed tải xong rồi đóng cửa sổ.");

        var window = new CaptureWindow(_settings, silent: false);
        window.Show();
        await window.Result;

        SetStatus("Mở requests.log, tìm request trả về moment, điền FeedUrlMarker vào settings.json rồi chạy lại.");
        Process.Start("explorer.exe", AppSettings.Dir);
    }

    async Task<bool> CaptureAsync(bool silent)
    {
        var window = new CaptureWindow(_settings, silent);
        window.Show();

        var result = await window.Result;
        if (result == null)
        {
            return false;
        }

        _session = result;
        return true;
    }

    void ShowImage(byte[] bytes)
    {
        var bitmap = new BitmapImage();

        using (var stream = new MemoryStream(bytes))
        {
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
        }

        bitmap.Freeze();

        card.Background = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
        status.Text = "";
        _hasImage = true;
    }

    void LoadCachedImage()
    {
        var path = Path.Combine(AppSettings.Dir, "latest.img");
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            ShowImage(File.ReadAllBytes(path));
        }
        catch
        {
            // cache is only a nicety
        }
    }

    // with an image on screen, errors go to the tooltip instead of covering the photo
    void SetStatus(string message)
    {
        if (_hasImage)
        {
            card.ToolTip = message;
        }
        else
        {
            status.Text = message;
        }
    }

    void OpenSite()
    {
        Process.Start(new ProcessStartInfo(_settings.SiteUrl) { UseShellExecute = true });
    }

    void SavePosition()
    {
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Save();
    }

    // drag to move, plain click to open the site
    void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var startLeft = Left;
        var startTop = Top;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var moved = Math.Abs(Left - startLeft) > 2 || Math.Abs(Top - startTop) > 2;
        if (moved)
        {
            SavePosition();
        }
        else
        {
            OpenSite();
        }
    }

    async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    async void Relogin_Click(object sender, RoutedEventArgs e)
    {
        if (await CaptureAsync(silent: false))
        {
            await RefreshAsync();
        }
    }

    void Top_Click(object sender, RoutedEventArgs e)
    {
        _settings.AlwaysOnTop = topMenu.IsChecked;
        Topmost = topMenu.IsChecked;
        _settings.Save();
    }

    void Folder_Click(object sender, RoutedEventArgs e)
    {
        Process.Start("explorer.exe", AppSettings.Dir);
    }

    void Exit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }
}
