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
        ApplyReadableAccent();
        DispatcherUnhandledException += OnUiException;
        Host = new AppHost();
        Host.Start();
    }

    /// <summary>
    /// Windows picks dark text for light accent colours, which makes switch knobs, accent buttons and the
    /// logo read as black. Use white text everywhere on accent, and darken the accent fill so it stays legible.
    /// </summary>
    void ApplyReadableAccent()
    {
        // Start from the colour the theme already uses (so the accent still matches Windows), then darken it.
        var (r, g, b) = ThemeInfo.AccentColor();
        if (TryFindResource("AccentFillColorDefaultBrush") is System.Windows.Media.SolidColorBrush cur)
            (r, g, b) = (cur.Color.R, cur.Color.G, cur.Color.B);
        const double k = 0.8;
        System.Windows.Media.Color Dark(double a) => System.Windows.Media.Color.FromArgb((byte)(255 * a), (byte)(r * k), (byte)(g * k), (byte)(b * k));
        System.Windows.Media.SolidColorBrush Brush(System.Windows.Media.Color c) { var br = new System.Windows.Media.SolidColorBrush(c); br.Freeze(); return br; }

        Resources["AccentFillColorDefaultBrush"] = Brush(Dark(1.0));
        Resources["AccentFillColorSecondaryBrush"] = Brush(Dark(0.9));
        Resources["AccentFillColorTertiaryBrush"] = Brush(Dark(0.8));
        foreach (var key in new[] { "TextOnAccentFillColorPrimaryBrush", "TextOnAccentFillColorSecondaryBrush" })
            Resources[key] = Brush(System.Windows.Media.Colors.White);
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
