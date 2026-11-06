using System;
using System.IO;
using System.Threading;
using Microsoft.UI.Xaml;

namespace LocketWidget;

public partial class App : Application
{
    // held for the whole run so a second copy (for example startup + manual start) exits at once
    static Mutex? _instance;

    MainWindow? _window;

    public App()
    {
        InitializeComponent();

        _instance = new Mutex(true, "LocketWidget.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Environment.Exit(0);
            return;
        }

        // the webview2 login profile lives here (same folder the WPF version used)
        Environment.SetEnvironmentVariable(
            "WEBVIEW2_USER_DATA_FOLDER",
            Path.Combine(AppSettings.Dir, "webview"));

        // a widget should not die because of one bad refresh
        UnhandledException += (_, e) =>
        {
            Log.Write(e.Exception.ToString());
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
