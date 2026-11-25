// XBar - GPL-3.0-or-later
using System.Runtime.InteropServices;
using XBar.Core.Native;

namespace XBar.Core.Engine;

public sealed record MonitorInfo(IntPtr Handle, string Device, RECT Bounds, RECT Work, bool IsPrimary);

public sealed record TaskbarInfo(IntPtr Hwnd, bool IsPrimary, RECT Rect, MonitorInfo Monitor, uint Dpi)
{
    public double Scale => Dpi / 96.0;
}

public static class Displays
{
    public static List<MonitorInfo> GetMonitors()
    {
        var list = new List<MonitorInfo>();
        Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr hdc, ref RECT r, IntPtr d) =>
        {
            var m = Get(h);
            if (m is not null) list.Add(m);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static MonitorInfo? Get(IntPtr hmon)
    {
        var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!Win32.GetMonitorInfo(hmon, ref mi)) return null;
        return new MonitorInfo(hmon, mi.szDevice, mi.rcMonitor, mi.rcWork, (mi.dwFlags & Win32.MONITORINFOF_PRIMARY) != 0);
    }

    /// <summary>Finds every Explorer taskbar (primary + secondaries) and the monitor it sits on.</summary>
    public static List<TaskbarInfo> GetTaskbars()
    {
        var list = new List<TaskbarInfo>();
        var main = Win32.FindWindow("Shell_TrayWnd", null);
        if (main != IntPtr.Zero) Add(main, true);

        IntPtr prev = IntPtr.Zero;
        for (int guard = 0; guard < 16; guard++)
        {
            var h = Win32.FindWindowEx(IntPtr.Zero, prev, "Shell_SecondaryTrayWnd", null);
            if (h == IntPtr.Zero) break;
            Add(h, false);
            prev = h;
        }
        return list;

        void Add(IntPtr hwnd, bool primary)
        {
            if (!Win32.GetWindowRect(hwnd, out var r)) return;
            // MonitorFromWindow is unreliable for a hidden auto-hide bar; use the rect centre clamped onto a monitor.
            var mon = Get(Win32.MonitorFromRect(ref r, Win32.MONITOR_DEFAULTTONEAREST));
            if (mon is null) return;
            list.Add(new TaskbarInfo(hwnd, primary, r, mon, Math.Max(96u, Win32.GetDpiForWindow(hwnd))));
        }
    }

    public static bool IsAutoHideOn()
    {
        var d = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        return ((long)Win32.SHAppBarMessage(Win32.ABM_GETSTATE, ref d) & Win32.ABS_AUTOHIDE) != 0;
    }

    public static uint ExplorerPid()
    {
        var tb = Win32.FindWindow("Shell_TrayWnd", null);
        if (tb == IntPtr.Zero) return 0;
        Win32.GetWindowThreadProcessId(tb, out uint pid);
        return pid;
    }
}
