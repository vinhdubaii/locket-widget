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

    // physical pixels per device independent pixel for the window's monitor
    public static double Scale(Window window)
    {
        var dpi = GetDpiForWindow(WindowNative.GetWindowHandle(window));
        return dpi == 0 ? 1.0 : dpi / 96.0;
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
