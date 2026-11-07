using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace LocketWidget;

[StructLayout(LayoutKind.Sequential)]
public struct POINT
{
    public int X;
    public int Y;
}

internal static class Native
{
    [DllImport("user32.dll")]
    static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);

    [DllImport("user32.dll")]
    static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

    // physical pixels per device independent pixel for the window's monitor
    public static double Scale(Window window)
    {
        var dpi = GetDpiForWindow(WindowNative.GetWindowHandle(window));
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    // windows 11 draws a thin border and its own rounded corners around every window.
    // the widget already has its own rounded corners, so switch both off
    // (calls fail harmlessly on older windows versions)
    public static void RemoveFrameDecorations(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);

        var doNotRound = 1; // DWMWCP_DONOTROUND
        DwmSetWindowAttribute(hwnd, 33, ref doNotRound, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE

        var noBorder = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        DwmSetWindowAttribute(hwnd, 34, ref noBorder, sizeof(int)); // DWMWA_BORDER_COLOR
    }

    // keeps the widget on screen while the user hovers "Show desktop" to peek at the desktop
    public static void ExcludeFromPeek(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);

        var yes = 1;
        DwmSetWindowAttribute(hwnd, 12, ref yes, sizeof(int)); // DWMWA_EXCLUDED_FROM_PEEK
    }

    // hard fallback: cut the window itself into a rounded rectangle so that nothing
    // (border, shadow, haze) can show outside it. the edge is not anti-aliased.
    public static void ClipToRoundedRect(Window window, int sizePx, int radiusPx)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var region = CreateRoundRectRgn(0, 0, sizePx + 1, sizePx + 1, radiusPx * 2, radiusPx * 2);

        // after this call the system owns the region
        SetWindowRgn(hwnd, region, true);
    }

    public static void ApplyIcon(Window window)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "icon.ico");
            if (File.Exists(path))
            {
                window.AppWindow.SetIcon(path);
            }
        }
        catch
        {
            // the icon is only cosmetic
        }
    }
}

internal static class Log
{
    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Dir);
            File.AppendAllText(
                Path.Combine(AppSettings.Dir, "crash.log"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine);
        }
        catch
        {
            // nothing else to do
        }
    }
}
