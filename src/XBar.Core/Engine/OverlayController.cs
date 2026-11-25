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

            var r = desired;
            bool ok = Win32.SystemParametersInfo(Win32.SPI_SETWORKAREA, 0, ref r, Win32.SPIF_SENDCHANGE);
            Log.Info($"SetWorkArea {mon.Device} {mon.Work} -> {desired} ({why}) ok={ok}");
            changed = true;
        }
        return changed;
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
