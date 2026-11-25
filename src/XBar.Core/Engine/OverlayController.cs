// XBar - GPL-3.0-or-later
using XBar.Core.Native;

namespace XBar.Core.Engine;

/// <summary>
/// Owns the monitor work areas. When active, each monitor that has a taskbar gets
/// a work area equal to the full monitor (minus an optional inset), so windows
/// maximise under the floating bar. Explorer may reset it; Verify() puts it back.
/// </summary>
public sealed class OverlayController
{
    readonly Queue<DateTime> _corrections = new();
    readonly HashSet<string> _recorded = new();

    public bool Active { get; private set; }
    public int InsetPx { get; set; }
    /// <summary>Monitors (device names) that must keep the classic reserved strip right now.</summary>
    public HashSet<string> ReservedMonitors { get; } = new();

    public int CorrectionsLastMinute
    {
        get { Trim(); return _corrections.Count; }
    }

    public event Action? GuardTripped;
    public const int MaxCorrectionsPerMinute = 30;

    public void Activate()
    {
        Active = true;
        Apply("activate");
    }

    public void Deactivate(string reason)
    {
        if (!Active) return;
        Active = false;
        RestoreService.RestoreWorkAreas(SafetyJournal.Read());
        SafetyJournal.Update(j => j.WorkAreaChanged = false);
        Log.Info($"Overlay deactivated ({reason})");
    }

    /// <summary>Applies desired work areas. Returns true if anything had to change.</summary>
    public bool Apply(string why)
    {
        if (!Active) return false;
        var taskbars = Displays.GetTaskbars();
        if (taskbars.Count == 0) return false;
        bool autoHide = Displays.IsAutoHideOn();
        bool changed = false;

        foreach (var mon in Displays.GetMonitors())
        {
            var tb = taskbars.FirstOrDefault(t => t.Monitor.Device == mon.Device);
            if (tb is null) continue;

            RECT desired = Desired(mon, taskbars, autoHide);
            if (mon.Work == desired) continue;

            // Write-ahead: remember the stock value before the first change on this monitor.
            if (_recorded.Add(mon.Device))
            {
                var stock = mon.Work;
                SafetyJournal.Update(j =>
                {
                    j.Monitors.RemoveAll(m => m.Device == mon.Device);
                    j.Monitors.Add(new MonitorRecord { Device = mon.Device, Bounds = mon.Bounds, OriginalWork = stock });
                    j.Dirty = true;
                    j.WorkAreaChanged = true;
                });
            }
            else
            {
                SafetyJournal.Update(j => { j.Dirty = true; j.WorkAreaChanged = true; });
            }

            // No SPIF_SENDCHANGE: Explorer recomputes (and undoes) the work area whenever it receives
            // the WM_SETTINGCHANGE broadcast (measured in the M1 spike: reset within ~300 ms, every time).
            // Instead we notify everyone except Explorer and re-fit maximised windows ourselves.
            var r = desired;
            bool ok = Win32.SystemParametersInfo(Win32.SPI_SETWORKAREA, 0, ref r, 0);
            Log.Info($"SetWorkArea {mon.Device} {mon.Work} -> {desired} ({why}) ok={ok}");
            if (ok)
            {
                NotifyNonExplorerWindows();
                RefitMaximized(mon, desired);
            }
            changed = true;
        }
        return changed;
    }

    static void NotifyNonExplorerWindows()
    {
        uint explorer = Displays.ExplorerPid();
        Win32.EnumWindows((h, _) =>
        {
            Win32.GetWindowThreadProcessId(h, out uint pid);
            if (pid != explorer && pid != (uint)Environment.ProcessId)
                Win32.PostMessage(h, Win32.WM_SETTINGCHANGE, new IntPtr(Win32.SPI_SETWORKAREA), IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
    }

    /// <summary>Already-maximised windows keep their old size until told; resize them to the new work area.</summary>
    static void RefitMaximized(MonitorInfo mon, RECT work)
    {
        int n = 0;
        Win32.EnumWindows((h, _) =>
        {
            try
            {
                if (!Win32.IsWindowVisible(h) || !Win32.IsZoomed(h) || Win32.IsCloaked(h)) return true;
                if (Win32.MonitorFromWindow(h, Win32.MONITOR_DEFAULTTONULL) != mon.Handle) return true;
                if (!Win32.GetWindowRect(h, out var wr)) return true;
                var fr = Win32.FrameBounds(h);
                // Invisible resize borders between the window rect and its visible frame.
                int l = fr.Left - wr.Left, t = fr.Top - wr.Top, rr = wr.Right - fr.Right, b = wr.Bottom - fr.Bottom;
                if (l < 0 || t < 0 || rr < 0 || b < 0 || l > 40 || t > 40 || rr > 40 || b > 40) { l = t = rr = b = 0; }
                var target = new RECT(work.Left - l, work.Top - t, work.Right + rr, work.Bottom + b);
                if (target == wr) return true;
                Win32.SetWindowPos(h, IntPtr.Zero, target.Left, target.Top, target.Width, target.Height,
                    0x0004 /*NOZORDER*/ | Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER | Win32.SWP_ASYNCWINDOWPOS);
                n++;
            }
            catch { }
            return true;
        }, IntPtr.Zero);
        if (n > 0) Log.Debug($"Refitted {n} maximised window(s) on {mon.Device}");
    }

    /// <summary>Checks for drift (e.g. Explorer reset it) and corrects it, with a loop guard.</summary>
    public void Verify(string why)
    {
        if (!Active) return;
        if (Apply(why))
        {
            _corrections.Enqueue(DateTime.UtcNow);
            Trim();
            if (_corrections.Count > MaxCorrectionsPerMinute)
            {
                Log.Error($"Work area fought back {_corrections.Count}x in a minute; tripping guard");
                _corrections.Clear();
                GuardTripped?.Invoke();
            }
        }
    }

    public RECT Desired(MonitorInfo mon, List<TaskbarInfo> taskbars, bool autoHide)
    {
        if (ReservedMonitors.Contains(mon.Device))
            return RestoreService.StockWorkArea(mon, taskbars, autoHide);
        var r = mon.Bounds;
        r.Bottom -= Math.Clamp(InsetPx, 0, 100);
        return r;
    }

    void Trim()
    {
        while (_corrections.Count > 0 && DateTime.UtcNow - _corrections.Peek() > TimeSpan.FromMinutes(1))
            _corrections.Dequeue();
    }
}
