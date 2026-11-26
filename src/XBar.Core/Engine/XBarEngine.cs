// XBar - GPL-3.0-or-later
using System.Windows.Threading;
using XBar.Core.Config;
using XBar.Core.Native;

namespace XBar.Core.Engine;

/// <summary>
/// The orchestrator. Runs on the UI thread. The host forwards window messages
/// (WndProc) from a top-level window so broadcasts like WM_SETTINGCHANGE and
/// TaskbarCreated arrive here.
/// </summary>
public sealed class XBarEngine : IDisposable
{
    readonly IntPtr _host;
    readonly Dispatcher _ui;
    readonly OverlayController _overlay = new();
    readonly ShapeController _shape = new();
    readonly List<IntPtr> _hooks = new();
    readonly List<WinEventProc> _hookProcs = new(); // keep delegates alive
    readonly DispatcherTimer _refreshDebounce, _verifyTimer, _geometryTimer, _mouseTimer, _peekTimer, _structuralDebounce;
    readonly uint _taskbarCreatedMsg;
    readonly Dictionary<IntPtr, TaskbarGeometry> _geometry = new();
    readonly Queue<DateTime> _explorerRestarts = new();
    HotkeyManager? _hotkeys;
    List<TaskbarInfo> _taskbars = new();
    List<MonitorInfo> _monitors = new();
    bool _geometryBusy, _peeking, _started, _disposed;
    DateTime _lastMouseActivity = DateTime.UtcNow;
    POINT _lastCursor;
    readonly Dictionary<IntPtr, double> _opacity = new();
    readonly Dictionary<IntPtr, string?> _hideReasons = new();

    public Settings Settings { get; private set; }
    public IAppearanceSink Appearance { get; set; } = new NullAppearanceSink();
    public bool Paused { get; private set; }
    public string PauseReason { get; private set; } = "";
    public Snapshot? LastSnapshot { get; private set; }
    public Dictionary<IntPtr, TaskbarState> States { get; } = new();
    public Dictionary<IntPtr, List<Pill>> LastPills { get; } = new();
    public IReadOnlyList<TaskbarInfo> Taskbars => _taskbars;
    public OverlayController Overlay => _overlay;
    public IReadOnlyList<string> FailedHotkeys => _hotkeys?.Failed ?? (IReadOnlyList<string>)Array.Empty<string>();
    public bool AutoHideConflict { get; private set; }

    public event Action? StatusChanged;
    public event Action<string>? Notify;
    public event Action? ProfileCycleRequested;

    public XBarEngine(IntPtr hostHwnd, Settings settings)
    {
        _host = hostHwnd;
        _ui = Dispatcher.CurrentDispatcher;
        Settings = settings;
        _taskbarCreatedMsg = Win32.RegisterWindowMessage("TaskbarCreated");

        _refreshDebounce = Timer(30, () => { _refreshDebounce!.Stop(); Refresh("debounced"); });
        _structuralDebounce = Timer(250, () => { _structuralDebounce!.Stop(); Rebuild("structural"); });
        _verifyTimer = Timer(1500, () => { _overlay.Verify("timer"); Refresh("timer"); });
        _geometryTimer = Timer(700, PollGeometry);
        _mouseTimer = Timer(100, MouseTick);
        _peekTimer = Timer(40, PeekTick);

        _overlay.GuardTripped += () =>
        {
            Pause("Explorer kept resetting the work area (loop guard)");
            Notify?.Invoke("XBar paused: Explorer kept fighting the overlay. Check the log.");
        };
    }

    static DispatcherTimer Timer(int ms, Action tick)
    {
        var t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => tick();
        return t;
    }

    // ------------------------------------------------------------------ lifecycle

    public void Start()
    {
        if (_started) return;
        _started = true;
        Log.Info("Engine starting");

        Win32.WTSRegisterSessionNotification(_host, 0);
        InstallHooks();
        RegisterHotkeys();
        Rebuild("start");
        _verifyTimer.Start();
        _geometryTimer.Start();
        UpdateMouseTimer();
    }

    public void ApplySettings(Settings s)
    {
        Settings = s;
        RegisterHotkeys();
        UpdateMouseTimer();
        if (!Paused)
        {
            _shape.Forget();
            Rebuild("settings");
        }
        StatusChanged?.Invoke();
    }

    public void Pause(string reason)
    {
        if (Paused) return;
        Paused = true;
        PauseReason = reason;
        Log.Info($"Pause: {reason}");
        StopAll(restore: true);
        StatusChanged?.Invoke();
    }

    public void Resume()
    {
        if (!Paused) return;
        Paused = false;
        PauseReason = "";
        Log.Info("Resume");
        _shape.ResetJournalFlag();
        Rebuild("resume");
        StatusChanged?.Invoke();
    }

    public void TogglePause()
    {
        if (Paused) Resume(); else Pause("paused by user");
    }

    /// <summary>Panic: restore everything immediately and stay paused.</summary>
    public void Panic()
    {
        Log.Warn("PANIC restore requested");
        Paused = true;
        PauseReason = "panic restore";
        StopAll(restore: true);
        RestoreService.RestoreAll("panic");
        StatusChanged?.Invoke();
        Notify?.Invoke("Taskbar restored. XBar is paused. Resume it from the tray.");
    }

    public void ToggleOverlay()
    {
        Settings.Overlay.Enabled = !Settings.Overlay.Enabled;
        Rebuild("toggle overlay");
        Notify?.Invoke(Settings.Overlay.Enabled ? "Overlay on: windows use the full screen" : "Reserve mode: classic taskbar strip");
        StatusChanged?.Invoke();
    }

    void StopAll(bool restore)
    {
        if (!restore) return;
        try { Appearance.RestoreAll(); } catch (Exception ex) { Log.Error("Appearance restore failed", ex); }
        try { _shape.ClearAll(); } catch (Exception ex) { Log.Error("Region restore failed", ex); }
        try { _overlay.Deactivate("stop"); } catch (Exception ex) { Log.Error("Work area restore failed", ex); }
        _opacity.Clear();
        LastPills.Clear();
    }

    /// <summary>Graceful shutdown: undo everything and mark the journal clean.</summary>
    public void Shutdown()
    {
        Log.Info("Engine shutdown");
        _verifyTimer.Stop(); _geometryTimer.Stop(); _mouseTimer.Stop(); _peekTimer.Stop();
        UninstallHooks();
        _hotkeys?.Clear();
        StopAll(restore: true);
        RestoreService.RestoreAll("shutdown");
        Win32.WTSUnRegisterSessionNotification(_host);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Shutdown();
    }

    // ------------------------------------------------------------------ messages

    /// <summary>Host window procedure hook. Returns true when handled.</summary>
    public bool WndProc(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (_hotkeys?.Handle(msg, wParam) == true) return true;

        if (msg == (int)_taskbarCreatedMsg && _taskbarCreatedMsg != 0)
        {
            OnExplorerRestarted();
            return true;
        }

        switch (msg)
        {
            case Win32.WM_SETTINGCHANGE:
                if (wParam.ToInt64() == Win32.SPI_SETWORKAREA)
                {
                    // Explorer (or someone) changed the work area. Verify right away (Apply is idempotent).
                    _ui.BeginInvoke(() => { _overlay.Verify("WM_SETTINGCHANGE/SPI_SETWORKAREA"); ScheduleRefresh(); });
                }
                else
                {
                    // Theme / taskbar alignment / accent changes.
                    _structuralDebounce.Stop(); _structuralDebounce.Start();
                }
                break;
            case Win32.WM_DISPLAYCHANGE:
            case Win32.WM_DPICHANGED:
                Log.Info("Display change");
                _structuralDebounce.Stop(); _structuralDebounce.Start();
                DelayedRebuild(1500, "display change settle");
                break;
            case Win32.WM_POWERBROADCAST:
                if (wParam.ToInt32() is Win32.PBT_APMRESUMEAUTOMATIC or Win32.PBT_APMRESUMESUSPEND)
                {
                    Log.Info("Resume from sleep");
                    DelayedRebuild(1500, "resume");
                    DelayedRebuild(5000, "resume late");
                }
                break;
            case Win32.WM_WTSSESSION_CHANGE:
                if (wParam.ToInt32() is Win32.WTS_SESSION_UNLOCK or Win32.WTS_CONSOLE_CONNECT or Win32.WTS_REMOTE_CONNECT)
                {
                    Log.Info($"Session change {wParam}");
                    DelayedRebuild(1000, "session");
                }
                break;
        }
        return false;
    }

    void DelayedRebuild(int ms, string why)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => { t.Stop(); if (!Paused) Rebuild(why); };
        t.Start();
    }

    void OnExplorerRestarted()
    {
        Log.Warn("TaskbarCreated: Explorer (re)started");
        _explorerRestarts.Enqueue(DateTime.UtcNow);
        while (_explorerRestarts.Count > 0 && DateTime.UtcNow - _explorerRestarts.Peek() > TimeSpan.FromSeconds(30))
            _explorerRestarts.Dequeue();

        _shape.Forget();
        _geometry.Clear();
        Appearance.OnTaskbarRecreated();

        if (_explorerRestarts.Count >= 3)
        {
            Pause("Explorer restarted 3 times in 30 s (crash-loop guard)");
            RestoreService.RestoreAll("explorer crash loop");
            Notify?.Invoke("Explorer is restarting repeatedly, so XBar paused itself and restored the taskbar.");
            return;
        }
        DelayedRebuild(1200, "explorer restart");
    }

    // ------------------------------------------------------------------ hooks

    void InstallHooks()
    {
        Hook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND);
        Hook(Win32.EVENT_SYSTEM_MOVESIZEEND, Win32.EVENT_SYSTEM_MOVESIZEEND);
        Hook(Win32.EVENT_SYSTEM_MINIMIZESTART, Win32.EVENT_SYSTEM_MINIMIZEEND);
        Hook(Win32.EVENT_OBJECT_DESTROY, Win32.EVENT_OBJECT_HIDE);
        Hook(Win32.EVENT_OBJECT_LOCATIONCHANGE, Win32.EVENT_OBJECT_LOCATIONCHANGE);
        Hook(Win32.EVENT_OBJECT_CLOAKED, Win32.EVENT_OBJECT_UNCLOAKED);
    }

    void Hook(uint min, uint max)
    {
        WinEventProc proc = OnWinEvent;
        _hookProcs.Add(proc);
        var h = Win32.SetWinEventHook(min, max, IntPtr.Zero, proc, 0, 0, Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS);
        if (h != IntPtr.Zero) _hooks.Add(h);
        else Log.Warn($"SetWinEventHook {min:X} failed");
    }

    void UninstallHooks()
    {
        foreach (var h in _hooks) Win32.UnhookWinEvent(h);
        _hooks.Clear();
    }

    void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (Paused || idObject != Win32.OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero) return;
        if (evt == Win32.EVENT_OBJECT_LOCATIONCHANGE)
        {
            // Only top-level windows matter (maximise/restore/fullscreen transitions).
            if (Win32.GetAncestor(hwnd, Win32.GA_ROOT) != hwnd) return;
            foreach (var tb in _taskbars)
                if (tb.Hwnd == hwnd) { _structuralDebounce.Stop(); _structuralDebounce.Start(); return; }
        }
        if (evt == Win32.EVENT_SYSTEM_FOREGROUND && Settings.Overlay.KeepOnTop) EnsureTopmost();
        ScheduleRefresh();
    }

    void ScheduleRefresh()
    {
        if (Paused) return;
        if (!_refreshDebounce.IsEnabled) _refreshDebounce.Start();
    }

    // ------------------------------------------------------------------ core pipeline

    /// <summary>Re-reads monitors/taskbars and applies everything.</summary>
    public void Rebuild(string why)
    {
        if (Paused || _disposed) return;
        try
        {
            _monitors = Displays.GetMonitors();
            _taskbars = Displays.GetTaskbars();
            AutoHideConflict = Displays.IsAutoHideOn();
            if (AutoHideConflict)
                Log.Warn("Taskbar auto-hide is ON. XBar's overlay expects it off (Settings > Personalization > Taskbar).");

            foreach (var dead in _geometry.Keys.Where(k => !_taskbars.Any(t => t.Hwnd == k)).ToList())
                _geometry.Remove(dead);

            _overlay.InsetPx = Settings.Overlay.BottomInsetPx;
            bool wantOverlay = Settings.Overlay.Enabled;
            if (wantOverlay && !_overlay.Active) _overlay.Activate();
            else if (!wantOverlay && _overlay.Active) _overlay.Deactivate("overlay disabled");

            PollGeometry();
            Refresh(why);
        }
        catch (Exception ex) { Log.Error($"Rebuild ({why}) failed", ex); }
    }

    void Refresh(string why)
    {
        if (Paused || _disposed || _taskbars.Count == 0) return;
        try
        {
            var snap = WindowTracker.Take(_monitors);
            LastSnapshot = snap;

            // Reserve-space rules switch a monitor back to the classic strip.
            var reserved = new HashSet<string>();
            foreach (var rule in Settings.Rules.Where(r => r.Enabled && r.Action == RuleAction.ReserveSpace))
            {
                foreach (var ms in snap.Monitors.Values)
                {
                    var pool = rule.OnlyWhenMaximized ? ms.Maximized : ms.Visible;
                    // Only the foreground app triggers it, so background windows don't keep the strip reserved.
                    if (snap.Foreground is { } fg && pool.Any(w => w.Hwnd == fg.Hwnd) && WindowTracker.Matches(rule, fg))
                        reserved.Add(ms.Device);
                }
            }
            if (!reserved.SetEquals(_overlay.ReservedMonitors))
            {
                _overlay.ReservedMonitors.Clear();
                _overlay.ReservedMonitors.UnionWith(reserved);
                _overlay.Apply("reserve rules");
            }

            foreach (var tb in _taskbars) ApplyTaskbar(tb, snap);
            StatusChanged?.Invoke();
        }
        catch (Exception ex) { Log.Error($"Refresh ({why}) failed", ex); }
    }

    void ApplyTaskbar(TaskbarInfo tb, Snapshot snap)
    {
        var ms = snap.For(tb.Monitor.Device);
        var fg = snap.Foreground;
        bool fgHere = fg is not null && fg.Device == tb.Monitor.Device;

        bool alwaysShow = fgHere && Settings.Rules.Any(r => r.Action == RuleAction.AlwaysShow && WindowTracker.Matches(r, fg!));
        bool hideRule = fgHere && Settings.Rules.Any(r => r.Action == RuleAction.HideBar && WindowTracker.Matches(r, fg!));
        bool fullscreen = ms.Fullscreen is not null && Settings.Overlay.HideOnFullscreen && !alwaysShow;

        string? hideReason = fullscreen ? $"fullscreen: {ms.Fullscreen!.Process} '{ms.Fullscreen.Title}'"
            : hideRule ? $"hide rule: {fg!.Process}"
            : (_peeking && Settings.Behavior.PeekEnabled && !Appearance.IsAvailable) ? "peek" : null;
        _hideReasons.TryGetValue(tb.Hwnd, out var prevReason);
        if (hideReason != prevReason)
        {
            Log.Info(hideReason is null ? $"Bar shown on {tb.Monitor.Device}" : $"Bar hidden on {tb.Monitor.Device} ({hideReason})");
            _hideReasons[tb.Hwnd] = hideReason;
        }
        if (hideReason is not null)
        {
            _shape.Hide(tb.Hwnd);
            LastPills[tb.Hwnd] = new();
            return;
        }

        // Dynamic state (TranslucentTB-style), highest priority first.
        var style = EffectiveStyle();
        var state = TaskbarState.Desktop;
        if (snap.TaskViewOpen && style.TaskViewOpen.Enabled) state = TaskbarState.TaskViewOpen;
        else if (snap.SearchOpen && fgHere && style.SearchOpen.Enabled) state = TaskbarState.SearchOpen;
        else if (snap.StartOpen && fgHere && style.StartOpen.Enabled) state = TaskbarState.StartOpen;
        else if (ms.HasMaximized && style.MaximizedWindow.Enabled) state = TaskbarState.MaximizedWindow;
        else if (ms.HasVisible && style.VisibleWindow.Enabled) state = TaskbarState.VisibleWindow;
        States[tb.Hwnd] = state;

        var look = style.For(state).Clone();
        var ruleLook = fgHere ? Settings.Rules.FirstOrDefault(r => r.Action == RuleAction.Appearance && r.Appearance is not null && WindowTracker.Matches(r, fg!)) : null;
        if (ruleLook?.Appearance is { } rl) look = rl.Clone();
        if (style.SyncAccentColor)
        {
            var (a, _, _, _) = ColorText.Parse(look.Color, (0x80, 0, 0, 0));
            var (r, g, b) = ThemeInfo.AccentColor();
            look.Color = ColorText.Format(a, r, g, b);
        }

        // Shape
        var shape = Settings.Shape;
        bool fill = shape.Mode == ShapeMode.Single && shape.FillWhenMaximized && ms.HasMaximized;
        bool includeTray = shape.ShowTray && (!Settings.Behavior.TrayOnHoverOnly || CursorOverTray(tb));
        _geometry.TryGetValue(tb.Hwnd, out var geo);
        if (geo.Taskbar.IsEmpty) geo = new TaskbarGeometry(tb.Rect, default, default);
        geo = geo with { Taskbar = tb.Rect };

        // Float: the bar's window can't grow, so floating leaves a gap under the pills and the pills
        // (centred in the strip above it) lose that much height. The icons are moved up by half the
        // gap so they stay centred. Only possible with the TAP (it moves the XAML content); the strip
        // never shrinks below a usable 32 DIP.
        int lift = Appearance.IsAvailable && !fill && shape.Mode != ShapeMode.Stock ? (int)Math.Round(shape.FloatOffset * tb.Scale) : 0;
        lift = Math.Clamp(lift, 0, Math.Max(0, tb.Rect.Height - (int)(32 * tb.Scale)));
        int contentShift = lift / 2;

        var pills = PillLayout.Compute(geo, shape, tb.Scale, includeTray, fill, lift);

        // The window region is the hit-test + clip mask. When the TAP paints anti-aliased pills
        // (with shadow/glow), the mask must be a little larger than the pills.
        int pad = 0;
        if (Appearance.IsAvailable)
        {
            pad = 1;
            // Keep this small: everything inside the region but outside the pills shows raw taskbar
            // content (e.g. the Show Desktop sliver). 3 px is enough for a soft edge shadow.
            if (style.ShadowEnabled || style.GlowEnabled) pad = 3;
        }
        var regionPills = pills.Select(p =>
        {
            var r = p.Rect;
            r.Left -= pad; r.Right += pad;
            r.Top = Math.Max(0, r.Top - pad); r.Bottom = Math.Min(tb.Rect.Height, r.Bottom + pad);
            return p with { Rect = r, Radius = p.Radius + pad };
        }).ToList();

        if (shape.Mode == ShapeMode.Stock) _shape.Clear(tb.Hwnd);
        else if (regionPills.Count > 0) _shape.Apply(tb.Hwnd, regionPills);
        LastPills[tb.Hwnd] = pills;

        if (Settings.Overlay.KeepOnTop) EnsureTopmost(tb.Hwnd);

        // The TAP's pill canvas lives inside the content it shifts, so pre-compensate the drawn pills.
        var drawPills = contentShift == 0 ? pills
            : pills.Select(p => { var r = p.Rect; r.Top += contentShift; r.Bottom += contentShift; return p with { Rect = r }; }).ToList();
        Appearance.Apply(new AppearanceSpec(tb.Hwnd, look, style, drawPills, tb.Scale,
            contentShift, Settings.Behavior.AnimationsEnabled ? Settings.Behavior.AnimationMs : 0));
    }

    StyleSettings EffectiveStyle()
    {
        var s = Settings.Style;
        if (s.AutoLightDark && s.LightVariant is not null && ThemeInfo.SystemUsesLightTheme()) return s.LightVariant;
        return s;
    }

    void EnsureTopmost(IntPtr? only = null)
    {
        if (Paused) return;
        foreach (var tb in _taskbars)
        {
            if (only is { } o && o != tb.Hwnd) continue;
            if (LastSnapshot?.For(tb.Monitor.Device).Fullscreen is not null && Settings.Overlay.HideOnFullscreen) continue;
            Win32.SetWindowPos(tb.Hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER | Win32.SWP_ASYNCWINDOWPOS);
        }
    }

    // ------------------------------------------------------------------ geometry

    void PollGeometry()
    {
        if (Paused || _geometryBusy || _taskbars.Count == 0) return;
        _geometryBusy = true;
        var tbs = _taskbars.ToList();
        Task.Run(() =>
        {
            var results = new List<(IntPtr, TaskbarGeometry)>();
            foreach (var tb in tbs)
            {
                if (!Win32.GetWindowRect(tb.Hwnd, out var r)) continue;
                var g = GeometryProvider.Query(tb with { Rect = r });
                if (g is { } gg) results.Add((tb.Hwnd, gg));
            }
            return results;
        }).ContinueWith(t =>
        {
            _ui.BeginInvoke(() =>
            {
                _geometryBusy = false;
                if (t.IsFaulted || Paused) return;
                bool changed = false;
                foreach (var (h, g) in t.Result)
                {
                    if (!_geometry.TryGetValue(h, out var old) || old != g)
                    {
                        _geometry[h] = g;
                        changed = true;
                    }
                }
                if (changed)
                {
                    // Taskbar itself may have moved/resized (e.g. alignment, size change).
                    _taskbars = Displays.GetTaskbars();
                    Refresh("geometry");
                }
            });
        });
    }

    // ------------------------------------------------------------------ mouse: idle dim / hover tray

    void UpdateMouseTimer()
    {
        bool need = Settings.Behavior.IdleDimEnabled || Settings.Behavior.TrayOnHoverOnly;
        if (need && !_mouseTimer.IsEnabled) _mouseTimer.Start();
        if (!need && _mouseTimer.IsEnabled)
        {
            _mouseTimer.Stop();
            foreach (var tb in _taskbars) SetOpacity(tb.Hwnd, 1.0);
        }
    }

    void MouseTick()
    {
        if (Paused) return;
        Win32.GetCursorPos(out var p);
        bool moved = p.X != _lastCursor.X || p.Y != _lastCursor.Y;
        _lastCursor = p;

        foreach (var tb in _taskbars)
        {
            bool over = tb.Rect.Contains(p);
            if (over && moved) _lastMouseActivity = DateTime.UtcNow;

            if (Settings.Behavior.IdleDimEnabled && !_peeking)
            {
                bool idle = !over && DateTime.UtcNow - _lastMouseActivity > TimeSpan.FromMilliseconds(Settings.Behavior.IdleDelayMs);
                SetOpacity(tb.Hwnd, idle ? Settings.Behavior.IdleOpacity : 1.0);
            }
        }

        if (Settings.Behavior.TrayOnHoverOnly && moved) ScheduleRefresh();
    }

    bool CursorOverTray(TaskbarInfo tb)
    {
        if (!_geometry.TryGetValue(tb.Hwnd, out var g) || g.Tray.IsEmpty) return false;
        Win32.GetCursorPos(out var p);
        var zone = g.Tray;
        zone.Top = tb.Rect.Top; zone.Bottom = tb.Rect.Bottom; zone.Right = tb.Rect.Right;
        return zone.Contains(p);
    }

    void SetOpacity(IntPtr tb, double value)
    {
        if (_opacity.TryGetValue(tb, out var cur) && Math.Abs(cur - value) < 0.001) return;
        _opacity[tb] = value;
        Appearance.SetOpacity(tb, value, Settings.Behavior.AnimationsEnabled ? Settings.Behavior.AnimationMs : 0);
    }

    // ------------------------------------------------------------------ hotkeys / peek

    void RegisterHotkeys()
    {
        _hotkeys ??= new HotkeyManager(_host);
        _hotkeys.Clear();
        var h = Settings.Hotkeys;
        _hotkeys.Register(Hotkey.Parse(h.Panic), Panic);
        _hotkeys.Register(Hotkey.Parse(h.TogglePause), TogglePause);
        _hotkeys.Register(Hotkey.Parse(h.ToggleOverlay), ToggleOverlay);
        _hotkeys.Register(Hotkey.Parse(h.NextProfile), () => ProfileCycleRequested?.Invoke());
        _hotkeys.Register(Hotkey.Parse(h.Peek), StartPeek);
    }

    void StartPeek()
    {
        if (Paused || !Settings.Behavior.PeekEnabled || _peeking) return;
        _peeking = true;
        Log.Debug("Peek start");
        if (Appearance.IsAvailable)
            foreach (var tb in _taskbars) { SetOpacity(tb.Hwnd, Settings.Behavior.PeekOpacity); _shape.Hide(tb.Hwnd); }
        else Refresh("peek");
        _peekTimer.Start();
    }

    void PeekTick()
    {
        if (Hotkey.Parse(Settings.Hotkeys.Peek).IsHeld()) return;
        _peekTimer.Stop();
        _peeking = false;
        Log.Debug("Peek end");
        foreach (var tb in _taskbars) SetOpacity(tb.Hwnd, 1.0);
        _shape.Forget();
        Refresh("peek end");
    }

    // ------------------------------------------------------------------ diagnostics

    public string Diagnostics()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"XBar diagnostics  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Paused: {Paused} {PauseReason}");
        sb.AppendLine($"Overlay active: {_overlay.Active}  inset={_overlay.InsetPx}  corrections/min={_overlay.CorrectionsLastMinute}  reserved=[{string.Join(",", _overlay.ReservedMonitors)}]");
        sb.AppendLine($"Auto-hide on: {Displays.IsAutoHideOn()}   Explorer pid: {Displays.ExplorerPid()}");
        sb.AppendLine($"Appearance: {Appearance.Status}");
        sb.Append(DiagnosticsReport.Describe(Displays.GetMonitors(), Displays.GetTaskbars(), _overlay));
        foreach (var tb in _taskbars)
        {
            States.TryGetValue(tb.Hwnd, out var st);
            _geometry.TryGetValue(tb.Hwnd, out var g);
            sb.AppendLine($"  taskbar {tb.Hwnd}: state={st} apps={g.Apps} tray={g.Tray}");
            if (LastPills.TryGetValue(tb.Hwnd, out var pills))
                foreach (var p in pills) sb.AppendLine($"    pill {p.Kind} {p.Rect} r={p.Radius}");
        }
        if (LastSnapshot?.Foreground is { } f)
            sb.AppendLine($"Foreground: {f.Process} '{f.Title}' [{f.Class}] max={f.Maximized} frame={f.Frame} fullscreen={LastSnapshot.For(f.Device).Fullscreen is not null}");
        return sb.ToString();
    }
}

public static class DiagnosticsReport
{
    public static string Describe(List<MonitorInfo> monitors, List<TaskbarInfo> taskbars, OverlayController? overlay = null)
    {
        var sb = new System.Text.StringBuilder();
        bool autoHide = Displays.IsAutoHideOn();
        foreach (var m in monitors)
        {
            sb.AppendLine($"Monitor {m.Device} primary={m.IsPrimary} bounds={m.Bounds} work={m.Work} stock={RestoreService.StockWorkArea(m, taskbars, autoHide)}" +
                          (overlay is { Active: true } ? $" desired={overlay.Desired(m, taskbars, autoHide)}" : ""));
        }
        foreach (var t in taskbars)
        {
            int rgn = Win32.GetWindowRgnBox(t.Hwnd, out var box);
            bool topmost = (Win32.ExStyle(t.Hwnd) & Win32.WS_EX_TOPMOST) != 0;
            sb.AppendLine($"Taskbar {t.Hwnd} primary={t.IsPrimary} rect={t.Rect} dpi={t.Dpi} topmost={topmost} region={(rgn == 0 ? "none" : box.ToString())}");
        }
        var j = SafetyJournal.Read();
        sb.AppendLine(j is null ? "Journal: none" : $"Journal: dirty={j.Dirty} pid={j.Pid} ownerAlive={SafetyJournal.IsOwnerAlive(j)} workArea={j.WorkAreaChanged} region={j.RegionChanged} tap={j.TapActive}");
        return sb.ToString();
    }
}
