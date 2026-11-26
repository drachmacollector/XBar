// XBar - GPL-3.0-or-later
using XBar.Core.Native;

namespace XBar.Core.Engine;

/// <summary>
/// Puts the taskbar and work areas back to stock. Safe to call from any
/// process, any number of times; every step is best-effort and independent.
/// </summary>
public static class RestoreService
{
    /// <summary>Optional hook so the running app can also tell the Explorer TAP to restore.</summary>
    public static Action? RestoreTapHook { get; set; }

    public static void RestoreAll(string reason)
    {
        Log.Warn($"RESTORE ALL ({reason})");
        var journal = SafetyJournal.Read();

        Try("tap", () => RestoreTapHook?.Invoke());
        Try("tap-broadcast", TapBroadcastRestore);
        Try("regions", RestoreRegions);
        Try("workarea", () => RestoreWorkAreas(journal));

        Try("journal", SafetyJournal.MarkClean);
        Log.Info("Restore complete");
    }

    public static void RestoreRegions()
    {
        foreach (var tb in Displays.GetTaskbars())
        {
            Win32.SetWindowRgn(tb.Hwnd, IntPtr.Zero, true);
        }
    }

    /// <summary>Ask any loaded XBar TAP inside Explorer to restore (works even if this process never loaded it).</summary>
    static void TapBroadcastRestore()
    {
        var tapWnd = Win32.FindWindow(TapProtocol.TapWindowClass, null);
        if (tapWnd != IntPtr.Zero)
            Win32.SendMessageTimeout(tapWnd, (int)Win32.RegisterWindowMessage(TapProtocol.RestoreMessage), IntPtr.Zero, IntPtr.Zero, Win32.SMTO_ABORTIFHUNG, 2000, out _);
    }

    public static void RestoreWorkAreas(JournalState? journal)
    {
        uint explorer = Displays.ExplorerPid();
        var taskbars = Displays.GetTaskbars();
        bool autoHide = Displays.IsAutoHideOn();

        foreach (var mon in Displays.GetMonitors())
        {
            RECT target = StockWorkArea(mon, taskbars, autoHide);

            // Prefer what we recorded before touching it (keeps other appbars' reservations),
            // but only if it was recorded against this same Explorer and monitor geometry.
            var rec = journal?.Monitors.FirstOrDefault(m => m.Device == mon.Device);
            if (rec is not null && journal!.ExplorerPid == explorer && rec.Bounds == mon.Bounds && mon.Bounds.Contains(rec.OriginalWork))
                target = rec.OriginalWork;

            if (mon.Work != target)
            {
                var r = target;
                bool ok = Win32.SystemParametersInfo(Win32.SPI_SETWORKAREA, 0, ref r, Win32.SPIF_SENDCHANGE);
                Log.Info($"Work area {mon.Device}: {mon.Work} -> {target} ok={ok}");
            }
        }
    }

    /// <summary>What Explorer itself would set: monitor minus the taskbar's strip on its edge (unless auto-hide).</summary>
    public static RECT StockWorkArea(MonitorInfo mon, List<TaskbarInfo> taskbars, bool autoHide)
    {
        var tb = taskbars.FirstOrDefault(t => t.Monitor.Device == mon.Device);
        if (tb is null || autoHide) return mon.Bounds;
        int t = tb.Thickness;
        int along = tb.Vertical ? mon.Bounds.Width : mon.Bounds.Height;
        if (t <= 0 || t > along / 3) return mon.Bounds;
        return ReserveEdge(mon.Bounds, tb.Edge, t);
    }

    /// <summary>The rect with <paramref name="px"/> removed from the given edge.</summary>
    public static RECT ReserveEdge(RECT r, TaskbarEdge edge, int px)
    {
        switch (edge)
        {
            case TaskbarEdge.Top: r.Top += px; break;
            case TaskbarEdge.Left: r.Left += px; break;
            case TaskbarEdge.Right: r.Right -= px; break;
            default: r.Bottom -= px; break;
        }
        return r;
    }

    static void Try(string step, Action a)
    {
        try { a(); }
        catch (Exception ex) { Log.Error($"Restore step '{step}' failed", ex); }
    }
}
