using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Dispatching;

namespace LocketWidget;

// Keeps the widget on screen when the user presses "Show desktop" (Win+D, taskbar corner).
//
// Windows does not minimize the widget. It raises the desktop window above everything that is
// not topmost, so the widget is simply covered. The only way to stay visible is to be topmost
// at that moment. This class watches which window is in front:
//
//   desktop in front       -> make the widget topmost (a few times in a row, because the
//                             desktop finishes raising itself a moment after the event)
//   normal app in front    -> put the widget back to normal, directly behind that app
//
// With "always on top" switched on the widget is topmost all the time and nothing is needed.
internal sealed class DesktopPin : IDisposable
{
    const uint EventSystemForeground = 0x0003;
    const uint WinEventOutOfContext = 0x0000;

    const int GwlExStyle = -20;
    const long WsExTopmost = 0x00000008;
    const long WsExToolWindow = 0x00000080;

    const uint SwpNoSize = 0x0001;
    const uint SwpNoMove = 0x0002;
    const uint SwpNoActivate = 0x0010;
    const uint KeepPlaceAndFocus = SwpNoSize | SwpNoMove | SwpNoActivate;

    static readonly IntPtr HwndTopmost = new(-1);
    static readonly IntPtr HwndNoTopmost = new(-2);

    // windows that can be in front without being a real application:
    // the shell, the taskbar, the start menu and the desktop itself
    static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "TaskListThumbnailWnd",
        "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland",
        "XamlExplorerHostIslandWindow",
        "Windows.UI.Core.CoreWindow",
        "MultitaskingViewFrame",
        "ForegroundStaging",
    };

    delegate void WinEventProc(
        IntPtr hook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint thread,
        uint time);

    [DllImport("user32.dll")]
    static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr module,
        WinEventProc callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    readonly IntPtr _widget;
    readonly Func<bool> _alwaysOnTop;
    readonly DispatcherQueueTimer _repeat;

    // native code keeps calling this delegate, so it must stay referenced for the whole run
    readonly WinEventProc _callback;

    IntPtr _hook;
    bool _raised;
    int _ticks;

    public DesktopPin(IntPtr widget, Func<bool> alwaysOnTop)
    {
        _widget = widget;
        _alwaysOnTop = alwaysOnTop;

        _repeat = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _repeat.Interval = TimeSpan.FromMilliseconds(150);
        _repeat.IsRepeating = true;
        _repeat.Tick += (_, _) =>
        {
            Raise();

            _ticks++;
            if (_ticks >= 4)
            {
                _repeat.Stop();
            }
        };

        _callback = OnForegroundChanged;
        _hook = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            IntPtr.Zero,
            _callback,
            0,
            0,
            WinEventOutOfContext);
    }

    void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero || hwnd == _widget)
        {
            return;
        }

        var className = ClassOf(hwnd);

        if (className == "Progman" || className == "WorkerW")
        {
            Raise();

            _ticks = 0;
            _repeat.Start();
        }
        else if (_raised && IsAppWindow(hwnd, className))
        {
            _repeat.Stop();
            Lower(hwnd);
        }
    }

    void Raise()
    {
        SetWindowPos(_widget, HwndTopmost, 0, 0, 0, 0, KeepPlaceAndFocus);
        _raised = true;
    }

    // back to a normal window, sitting directly behind the app that is now in front
    void Lower(IntPtr front)
    {
        _raised = false;

        if (_alwaysOnTop())
        {
            return;
        }

        SetWindowPos(_widget, HwndNoTopmost, 0, 0, 0, 0, KeepPlaceAndFocus);

        // an app that is itself topmost: stay above the normal windows and leave it there
        if ((GetExStyle(front) & WsExTopmost) != 0)
        {
            return;
        }

        SetWindowPos(_widget, front, 0, 0, 0, 0, KeepPlaceAndFocus);
    }

    static bool IsAppWindow(IntPtr hwnd, string className)
    {
        if (!IsWindowVisible(hwnd) || ShellClasses.Contains(className))
        {
            return false;
        }

        if ((GetExStyle(hwnd) & WsExToolWindow) != 0)
        {
            return false;
        }

        // the taskbar and many shell helpers have no title
        return GetWindowTextLength(hwnd) > 0;
    }

    static long GetExStyle(IntPtr hwnd)
    {
        return (long)GetWindowLongPtr(hwnd, GwlExStyle);
    }

    static string ClassOf(IntPtr hwnd)
    {
        var text = new StringBuilder(256);
        return GetClassName(hwnd, text, text.Capacity) > 0 ? text.ToString() : "";
    }

    public void Dispose()
    {
        _repeat.Stop();

        if (_hook != IntPtr.Zero)
        {
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
