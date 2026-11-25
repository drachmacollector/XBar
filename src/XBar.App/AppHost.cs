// XBar - GPL-3.0-or-later
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using XBar.App.UI;
using XBar.Core;
using XBar.Core.Config;
using XBar.Core.Engine;
using XBar.Core.Native;

namespace XBar.App;

/// <summary>Owns the engine, the hidden message window, tray icon, watchdog and control panel.</summary>
public sealed class AppHost : IDisposable
{
    HwndSource? _msgWindow;
    Process? _watchdog;
    readonly DispatcherTimer _watchdogTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    readonly DispatcherTimer _saveDebounce = new() { Interval = TimeSpan.FromMilliseconds(150) };
    uint _showPanelMsg, _panicMsg;
    ControlPanel? _panel;
    bool _disposed;

    public SettingsStore Store { get; } = new();
    public XBarEngine? Engine { get; private set; }
    public TrayIcon? Tray { get; private set; }
    public TapClient? Tap { get; private set; }
    public IntPtr MessageHwnd => _msgWindow?.Handle ?? IntPtr.Zero;

    public event Action? StatusChanged;

    public void Start()
    {
        _showPanelMsg = Win32.RegisterWindowMessage(Program.ShowPanelMessage);
        _panicMsg = Win32.RegisterWindowMessage(Program.PanicMessage);

        // Top-level (not message-only) so it receives broadcasts: WM_SETTINGCHANGE, TaskbarCreated, power...
        var p = new HwndSourceParameters(TapProtocol.AppWindowTitle)
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP, never shown
            ExtendedWindowStyle = 0x00000080,         // WS_EX_TOOLWINDOW
            Width = 0, Height = 0,
        };
        _msgWindow = new HwndSource(p);
        _msgWindow.AddHook(WndProc);

        Tray = new TrayIcon(this);

        Engine = new XBarEngine(MessageHwnd, Store.Current);
        Engine.StatusChanged += () => { Tray?.Refresh(); StatusChanged?.Invoke(); };
        Engine.Notify += msg => Tray?.ShowBalloon(msg);
        Engine.ProfileCycleRequested += CycleProfile;

        Tap = new TapClient(MessageHwnd);
        Engine.Appearance = Tap;
        RestoreService.RestoreTapHook = () => Tap?.RestoreAll();

        Store.ExternallyChanged += s => Application.Current.Dispatcher.BeginInvoke(() => Engine?.ApplySettings(s));
        _saveDebounce.Tick += (_, _) => { _saveDebounce.Stop(); try { Store.Save(Store.Current); } catch (Exception ex) { Log.Error("Save failed", ex); } };

        _watchdog = Watchdog.Launch();
        _watchdogTimer.Tick += (_, _) =>
        {
            if (_watchdog is null || _watchdog.HasExited)
            {
                Log.Warn("Watchdog not running; restarting it");
                _watchdog = Watchdog.Launch();
            }
        };
        _watchdogTimer.Start();

        Shortcuts.EnsureStartMenu();

        if (Program.SafeMode)
        {
            Log.Warn("Safe mode: no changes applied");
            Engine.Pause("safe mode (started with Shift or --safe)");
            ShowPanel();
        }
        else
        {
            Engine.Start();
            if (Store.Current.Overlay.Enabled || Store.Current.Style.Desktop.Enabled)
                _ = Tap.ConnectAsync();
        }

        if (Program.RecoveredFromCrash)
            Tray.ShowBalloon("XBar restored the taskbar after the previous session ended unexpectedly.");

        if (!Store.Current.FirstRunDone)
        {
            Store.Current.FirstRunDone = true;
            Store.Save(Store.Current);
            ShowPanel();
        }

        if (Engine.AutoHideConflict)
            Tray.ShowBalloon("Taskbar auto-hide is on. Turn it off (Settings > Personalization > Taskbar) so XBar can keep the bar visible.");
        if (Engine.FailedHotkeys.Count > 0)
            Tray.ShowBalloon($"Some hotkeys are taken by other apps: {string.Join(", ", Engine.FailedHotkeys)}");
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        try
        {
            if (msg == (int)_showPanelMsg) { ShowPanel(); handled = true; return IntPtr.Zero; }
            if (msg == (int)_panicMsg) { Engine?.Panic(); handled = true; return IntPtr.Zero; }
            if (msg == Win32.WM_COPYDATA && Tap is not null && Tap.HandleCopyData(wParam, lParam))
            {
                handled = true;
                return new IntPtr(1);
            }
            if (msg == Win32.WM_QUERYENDSESSION) { handled = true; return new IntPtr(1); }
            if (msg == Win32.WM_ENDSESSION && wParam != IntPtr.Zero)
            {
                Log.Info("Session ending; restoring");
                Engine?.Shutdown();
                handled = true;
                return IntPtr.Zero;
            }
            if (Engine?.WndProc(msg, wParam, lParam) == true) handled = true;
        }
        catch (Exception ex) { Log.Error($"WndProc {msg:X}", ex); }
        return IntPtr.Zero;
    }

    /// <summary>Called by the control panel on every edit: apply live, save shortly after.</summary>
    public void SettingsEdited()
    {
        Engine?.ApplySettings(Store.Current);
        if (Store.Current.Style.Desktop.Enabled && Tap is { IsAvailable: false, IsConnecting: false })
            _ = Tap.ConnectAsync();
        _saveDebounce.Stop();
        _saveDebounce.Start();
        StatusChanged?.Invoke();
    }

    public void ReplaceSettings(Settings s)
    {
        s.FirstRunDone = true;
        Store.Save(s);
        Engine?.ApplySettings(Store.Current);
        StatusChanged?.Invoke();
    }

    public void ApplyProfile(Profile p)
    {
        var s = Store.Current;
        s.Shape = SettingsStore.Clone(new Settings { Shape = p.Shape }).Shape;
        s.Style = SettingsStore.Clone(new Settings { Style = p.Style }).Style;
        s.Behavior = SettingsStore.Clone(new Settings { Behavior = p.Behavior }).Behavior;
        s.ActiveProfile = p.Name;
        SettingsEdited();
        _panel?.Reload();
    }

    void CycleProfile()
    {
        var list = Store.Current.Profiles;
        if (list.Count == 0) { Tray?.ShowBalloon("No profiles yet. Create one in the control panel."); return; }
        int i = list.FindIndex(x => x.Name == Store.Current.ActiveProfile);
        var next = list[(i + 1) % list.Count];
        ApplyProfile(next);
        Tray?.ShowBalloon($"Profile: {next.Name}");
    }

    public void ShowPanel()
    {
        if (_panel is null || !_panel.IsLoaded)
        {
            _panel = new ControlPanel(this);
            _panel.Closed += (_, _) => _panel = null;
            _panel.Show();
        }
        if (_panel.WindowState == WindowState.Minimized) _panel.WindowState = WindowState.Normal;
        _panel.Activate();
    }

    public void Exit()
    {
        Dispose();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Log.Info("Exiting");
        _watchdogTimer.Stop();
        try { Engine?.Shutdown(); } catch (Exception ex) { Log.Error("Shutdown", ex); }
        try { Tap?.Dispose(); } catch { }
        try { Store.Save(Store.Current); } catch { }
        Store.Dispose();
        Tray?.Dispose();
        _msgWindow?.Dispose();
        // The watchdog sees a clean journal and exits on its own.
    }
}
