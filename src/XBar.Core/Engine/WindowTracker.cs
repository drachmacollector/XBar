// XBar - GPL-3.0-or-later
using XBar.Core.Config;
using XBar.Core.Native;

namespace XBar.Core.Engine;

public sealed record WindowFacts(IntPtr Hwnd, string Class, string Title, string Process, RECT Frame, bool Maximized, string Device);

public sealed class MonitorSnapshot
{
    public required string Device { get; init; }
    public bool HasVisible { get; set; }
    public bool HasMaximized { get; set; }
    public WindowFacts? Fullscreen { get; set; }
    public List<WindowFacts> Maximized { get; } = new();
    public List<WindowFacts> Visible { get; } = new();
}

public sealed class Snapshot
{
    public Dictionary<string, MonitorSnapshot> Monitors { get; } = new();
    public WindowFacts? Foreground { get; set; }
    public bool StartOpen { get; set; }
    public bool SearchOpen { get; set; }
    public bool TaskViewOpen { get; set; }

    public MonitorSnapshot For(string device) =>
        Monitors.TryGetValue(device, out var m) ? m : new MonitorSnapshot { Device = device };
}

/// <summary>Classifies the windows on each monitor: visible / maximised / fullscreen, plus shell surfaces.</summary>
public static class WindowTracker
{
    static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW", "XamlExplorerHostIslandWindow",
        "Windows.UI.Core.CoreWindow", "TopLevelWindowForOverflowXamlIsland", "NotifyIconOverflowWindow",
        "Shell_InputSwitchTopLevelWindow", "ForegroundStaging", "MultitaskingViewFrame", "TaskListThumbnailWnd",
        "Xaml_WindowedPopupClass", "XBarWindow",
    };

    static readonly uint OwnPid = (uint)Environment.ProcessId;

    public static Snapshot Take(IReadOnlyList<MonitorInfo> monitors)
    {
        var snap = new Snapshot();
        foreach (var m in monitors) snap.Monitors[m.Device] = new MonitorSnapshot { Device = m.Device };

        var fg = Win32.GetAncestor(Win32.GetForegroundWindow(), Win32.GA_ROOT);
        if (fg != IntPtr.Zero)
        {
            snap.Foreground = Facts(fg, monitors);
            var proc = snap.Foreground?.Process ?? "";
            var cls = snap.Foreground?.Class ?? "";
            snap.StartOpen = proc.Equals("StartMenuExperienceHost.exe", StringComparison.OrdinalIgnoreCase);
            snap.SearchOpen = proc.Equals("SearchHost.exe", StringComparison.OrdinalIgnoreCase)
                           || proc.Equals("SearchApp.exe", StringComparison.OrdinalIgnoreCase);
            snap.TaskViewOpen = cls == "XamlExplorerHostIslandWindow" && (snap.Foreground?.Title ?? "").Contains("Task View", StringComparison.OrdinalIgnoreCase);
        }

        Win32.EnumWindows((h, _) =>
        {
            try { Classify(h, monitors, snap); } catch { }
            return true;
        }, IntPtr.Zero);

        // Fullscreen is decided by the foreground window only (like Explorer's own rude-window logic),
        // plus the shell's D3D / presentation-mode signal.
        if (snap.Foreground is { } f && IsFullscreen(f, monitors))
            snap.For(f.Device).Fullscreen = f;
        else if (snap.Foreground is { } f2 && Win32.SHQueryUserNotificationState(out int quns) == 0
                 && (quns == Win32.QUNS_RUNNING_D3D_FULL_SCREEN || quns == Win32.QUNS_PRESENTATION_MODE))
            snap.For(f2.Device).Fullscreen = f2;

        return snap;
    }

    static void Classify(IntPtr h, IReadOnlyList<MonitorInfo> monitors, Snapshot snap)
    {
        if (!Win32.IsWindowVisible(h) || Win32.IsIconic(h) || Win32.IsCloaked(h)) return;
        long style = Win32.Style(h), ex = Win32.ExStyle(h);
        if ((style & Win32.WS_CHILD) != 0) return;
        if ((ex & Win32.WS_EX_TOOLWINDOW) != 0 && (style & Win32.WS_CAPTION) != Win32.WS_CAPTION) return;
        Win32.GetWindowThreadProcessId(h, out uint pid);
        if (pid == OwnPid) return;
        var cls = Win32.ClassName(h);
        if (ShellClasses.Contains(cls)) return;

        var f = Facts(h, monitors);
        if (f is null || f.Frame.Width < 50 || f.Frame.Height < 50) return;
        if (!snap.Monitors.TryGetValue(f.Device, out var ms)) return;

        ms.HasVisible = true;
        ms.Visible.Add(f);
        if (f.Maximized) { ms.HasMaximized = true; ms.Maximized.Add(f); }
    }

    public static WindowFacts? Facts(IntPtr h, IReadOnlyList<MonitorInfo> monitors)
    {
        var frame = Win32.FrameBounds(h);
        var hmon = Win32.MonitorFromWindow(h, Win32.MONITOR_DEFAULTTONEAREST);
        var mon = monitors.FirstOrDefault(m => m.Handle == hmon) ?? Displays.Get(hmon);
        if (mon is null) return null;
        return new WindowFacts(h, Win32.ClassName(h), Win32.WindowText(h), ProcessName(h),
            frame, Win32.IsZoomed(h), mon.Device);
    }

    static readonly Dictionary<uint, string> NameCache = new();
    static DateTime _cacheReset = DateTime.UtcNow;

    static string ProcessName(IntPtr h)
    {
        Win32.GetWindowThreadProcessId(h, out uint pid);
        lock (NameCache)
        {
            if (DateTime.UtcNow - _cacheReset > TimeSpan.FromMinutes(2)) { NameCache.Clear(); _cacheReset = DateTime.UtcNow; }
            if (NameCache.TryGetValue(pid, out var n)) return n;
            var p = Win32.ProcessPath(pid);
            n = p is null ? "" : Path.GetFileName(p);
            NameCache[pid] = n;
            return n;
        }
    }

    public static bool IsFullscreen(WindowFacts f, IReadOnlyList<MonitorInfo> monitors)
    {
        if (ShellClasses.Contains(f.Class)) return false;
        var mon = monitors.FirstOrDefault(m => m.Device == f.Device);
        if (mon is null || !f.Frame.Contains(mon.Bounds)) return false;

        long style = Win32.Style(f.Hwnd);
        const long WS_THICKFRAME = 0x00040000;
        // A normal maximised window (has a frame or caption) is not "fullscreen", even when the
        // work area is the whole monitor. Borderless games, F11 browsers and video players are.
        if (f.Maximized && ((style & WS_THICKFRAME) != 0 || (style & Win32.WS_CAPTION) == Win32.WS_CAPTION))
            return false;
        return true;
    }

    public static bool Matches(AppRule rule, WindowFacts f) => rule.Enabled && !string.IsNullOrWhiteSpace(rule.Pattern) && rule.Match switch
    {
        RuleMatch.ProcessName => string.Equals(Path.GetFileNameWithoutExtension(f.Process), Path.GetFileNameWithoutExtension(rule.Pattern.Trim()), StringComparison.OrdinalIgnoreCase),
        RuleMatch.WindowClass => string.Equals(f.Class, rule.Pattern.Trim(), StringComparison.Ordinal),
        RuleMatch.TitleContains => f.Title.Contains(rule.Pattern.Trim(), StringComparison.OrdinalIgnoreCase),
        _ => false,
    };
}
