using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace LocketWidget;

public sealed partial class MainWindow : Window
{
    // WINCODEC_ERR_COMPONENTNOTFOUND: no decoder for the image format (WebP without the store extension)
    const int ComponentNotFound = unchecked((int)0x88982F50);

    readonly AppSettings _settings = AppSettings.Load();
    readonly FeedClient _feed;
    readonly DispatcherQueueTimer _timer;

    CapturedRequest? _session = SecureStore.Load();
    string? _lastUrl;
    bool _hasImage;
    bool _busy;

    // drag state
    bool _pressed;
    bool _moved;
    POINT _pressCursor;
    PointInt32 _pressPosition;

    public MainWindow()
    {
        InitializeComponent();

        Title = "Locket Widget";
        _feed = new FeedClient(_settings);

        ConfigureWindow();
        TopMenu.IsChecked = _settings.AlwaysOnTop;

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMinutes(Math.Max(1, _settings.RefreshMinutes));
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        Root.Loaded += async (_, _) =>
        {
            await LoadCachedImageAsync();
            await RefreshAsync();
        };
    }

    // borderless, no taskbar button, fixed size, placed where it was last time
    void ConfigureWindow()
    {
        var scale = Native.Scale(this);
        var size = (int)Math.Round(_settings.Size * scale);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = _settings.AlwaysOnTop;
        }

        AppWindow.IsShownInSwitchers = false;
        Native.ApplyIcon(this);

        int x;
        int y;

        if (_settings.Left is double left && _settings.Top is double top)
        {
            x = (int)Math.Round(left * scale);
            y = (int)Math.Round(top * scale);
        }
        else
        {
            // first run: bottom-right corner of the work area
            var area = DisplayArea.Primary.WorkArea;
            var margin = (int)Math.Round(24 * scale);
            x = area.X + area.Width - size - margin;
            y = area.Y + area.Height - size - margin;
        }

        // never start off-screen (monitor unplugged, resolution changed...)
        var work = DisplayArea.GetFromPoint(
            new PointInt32(x + size / 2, y + size / 2),
            DisplayAreaFallback.Nearest).WorkArea;

        x = Math.Clamp(x, work.X, Math.Max(work.X, work.X + work.Width - size));
        y = Math.Clamp(y, work.Y, Math.Max(work.Y, work.Y + work.Height - size));

        AppWindow.MoveAndResize(new RectInt32(x, y, size, size));

        Native.RemoveFrameDecorations(this);

        if (_settings.ClipRoundedWindow)
        {
            Native.ClipToRoundedRect(this, size, (int)Math.Round(32 * scale));
        }
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
            await ShowImageAsync(bytes);
            _lastUrl = url;
            SetTip("Cập nhật lúc " + DateTime.Now.ToString("HH:mm"));
        }
        catch (Exception ex) when (ex.HResult == ComponentNotFound)
        {
            SetStatus("Windows chưa giải mã được WebP. Cài 'WebP Image Extensions' từ Microsoft Store.");
        }
        catch (Exception ex)
        {
            Log.Write(ex.ToString());
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
        window.Activate();
        await window.Result;

        SetStatus("Mở requests.log, tìm request trả về moment, điền FeedUrlMarker vào settings.json rồi chạy lại.");
        Process.Start("explorer.exe", AppSettings.Dir);
    }

    async Task<bool> CaptureAsync(bool silent)
    {
        var window = new CaptureWindow(_settings, silent);
        window.Activate();

        var result = await window.Result;
        if (result == null)
        {
            return false;
        }

        _session = result;
        return true;
    }

    async Task ShowImageAsync(byte[] bytes)
    {
        var bitmap = new BitmapImage();

        using (var stream = new MemoryStream(bytes).AsRandomAccessStream())
        {
            await bitmap.SetSourceAsync(stream);
        }

        Card.Background = new ImageBrush
        {
            ImageSource = bitmap,
            Stretch = Stretch.UniformToFill,
        };

        Status.Text = "";
        _hasImage = true;
    }

    async Task LoadCachedImageAsync()
    {
        var path = Path.Combine(AppSettings.Dir, "latest.img");
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            await ShowImageAsync(await File.ReadAllBytesAsync(path));
        }
        catch
        {
            // cache is only a nicety
        }
    }

    void SetTip(string text)
    {
        ToolTipService.SetToolTip(Card, text);
    }

    // with an image on screen, errors go to the tooltip instead of covering the photo
    void SetStatus(string message)
    {
        if (_hasImage)
        {
            SetTip(message);
        }
        else
        {
            Status.Text = message;
        }
    }

    void OpenSite()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_settings.SiteUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Write(ex.ToString());
        }
    }

    void SavePosition()
    {
        var scale = Native.Scale(this);
        _settings.Left = AppWindow.Position.X / scale;
        _settings.Top = AppWindow.Position.Y / scale;
        _settings.Save();
    }

    // drag to move, plain click to open the site
    void Card_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(Card).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Native.GetCursorPos(out _pressCursor);
        _pressPosition = AppWindow.Position;
        _pressed = true;
        _moved = false;

        Card.CapturePointer(e.Pointer);
    }

    void Card_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed)
        {
            return;
        }

        // screen coordinates, so moving the window under the pointer does not cause jitter
        Native.GetCursorPos(out var cursor);
        var dx = cursor.X - _pressCursor.X;
        var dy = cursor.Y - _pressCursor.Y;

        if (!_moved && Math.Abs(dx) < 4 && Math.Abs(dy) < 4)
        {
            return;
        }

        _moved = true;
        AppWindow.Move(new PointInt32(_pressPosition.X + dx, _pressPosition.Y + dy));
    }

    void Card_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed)
        {
            return;
        }

        var moved = _moved;
        _pressed = false;
        _moved = false;
        Card.ReleasePointerCapture(e.Pointer);

        if (moved)
        {
            SavePosition();
        }
        else
        {
            OpenSite();
        }
    }

    void Card_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _pressed = false;
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
        _settings.AlwaysOnTop = TopMenu.IsChecked;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = TopMenu.IsChecked;
        }

        _settings.Save();
    }

    void Folder_Click(object sender, RoutedEventArgs e)
    {
        Process.Start("explorer.exe", AppSettings.Dir);
    }

    void Exit_Click(object sender, RoutedEventArgs e)
    {
        SavePosition();
        Application.Current.Exit();
    }
}
