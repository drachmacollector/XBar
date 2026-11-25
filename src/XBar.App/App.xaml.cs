// XBar - GPL-3.0-or-later
using System.Windows;
using System.Windows.Threading;
using XBar.Core;
using XBar.Core.Engine;

namespace XBar.App;

public partial class App : Application
{
    public static AppHost Host { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUiException;
        Host = new AppHost();
        Host.Start();
    }

    void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("UI thread exception", e.Exception);
        e.Handled = true;
        try
        {
            // Keep the process alive but get the desktop back to stock.
            Host?.Engine?.Pause("internal error (see log)");
            RestoreService.RestoreAll("ui exception");
            Host?.Tray?.ShowBalloon("XBar hit an error and paused itself. The taskbar was restored.");
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Host?.Dispose();
        base.OnExit(e);
    }
}
