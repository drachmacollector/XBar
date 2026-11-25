// XBar - GPL-3.0-or-later
using System.Windows.Automation;
using XBar.Core.Native;

namespace XBar.Core.Engine;

/// <summary>
/// Reads where the app buttons and tray actually are, using UI Automation on
/// Explorer's XAML taskbar. (The legacy ReBarWindow32/MSTaskSwWClass rects that
/// RoundedTB used are stale on Win11 24H2+.) The TAP can later push exact bounds.
/// </summary>
public static class GeometryProvider
{
    /// <summary>Set by the TAP client when it has fresher bounds (screen px).</summary>
    public static Func<IntPtr, TaskbarGeometry?>? TapOverride { get; set; }

    public static TaskbarGeometry? Query(TaskbarInfo tb)
    {
        var fromTap = TapOverride?.Invoke(tb.Hwnd);
        if (fromTap is not null) return fromTap;

        try
        {
            var task = Task.Run(() => QueryUia(tb));
            if (!task.Wait(TimeSpan.FromSeconds(2)))
            {
                Log.Warn("UIA geometry query timed out");
                return null;
            }
            return task.Result;
        }
        catch (Exception ex)
        {
            Log.Warn($"UIA geometry query failed: {ex.GetBaseException().Message}");
            return null;
        }
    }

    static TaskbarGeometry? QueryUia(TaskbarInfo tb)
    {
        var root = AutomationElement.FromHandle(tb.Hwnd);
        if (root is null) return null;

        RECT apps = default, tray = default;
        var walker = TreeWalker.ControlViewWalker;

        void Visit(AutomationElement e, int depth)
        {
            if (depth > 4) return;
            for (var c = walker.GetFirstChild(e); c is not null; c = walker.GetNextSibling(c))
            {
                AutomationElement.AutomationElementInformation cur;
                try { cur = c.Current; } catch { continue; }
                string cls = cur.ClassName ?? "";
                string id = cur.AutomationId ?? "";
                var b = cur.BoundingRectangle;
                bool hasRect = !b.IsEmpty && b.Width > 0 && b.Height > 0;

                if (id == "TaskbarFrame")
                {
                    // Children of the frame are Start, Search, Task View, pinned / running apps.
                    for (var a = walker.GetFirstChild(c); a is not null; a = walker.GetNextSibling(a))
                    {
                        try
                        {
                            var ai = a.Current;
                            if (ai.AutomationId == "WidgetsButton") continue; // own segment, far left
                            var ab = ai.BoundingRectangle;
                            if (ab.IsEmpty || ab.Width <= 0) continue;
                            apps = Union(apps, ToRect(ab));
                        }
                        catch { }
                    }
                    continue;
                }

                if (cls.StartsWith("SystemTray.", StringComparison.Ordinal))
                {
                    // Show Desktop is a sliver at the screen edge; keep it out of the pill.
                    if (hasRect && cls != "SystemTray.ShowDesktopButton")
                        tray = Union(tray, ToRect(b));
                    continue;
                }

                Visit(c, depth + 1);
            }
        }

        Visit(root, 0);

        // Content rects vertically follow the taskbar, not the buttons.
        if (!apps.IsEmpty) { apps.Top = tb.Rect.Top; apps.Bottom = tb.Rect.Bottom; }
        if (!tray.IsEmpty) { tray.Top = tb.Rect.Top; tray.Bottom = tb.Rect.Bottom; }

        // Fallback for the tray: the legacy TrayNotifyWnd still tracks the tray's left edge.
        if (tray.IsEmpty && tb.IsPrimary)
        {
            var tn = Win32.FindWindowEx(tb.Hwnd, IntPtr.Zero, "TrayNotifyWnd", null);
            if (tn != IntPtr.Zero && Win32.GetWindowRect(tn, out var tr)) tray = tr;
        }

        return new TaskbarGeometry(tb.Rect, apps, tray);
    }

    static RECT ToRect(System.Windows.Rect r) =>
        new((int)Math.Round(r.Left), (int)Math.Round(r.Top), (int)Math.Round(r.Right), (int)Math.Round(r.Bottom));

    static RECT Union(RECT a, RECT b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        return new RECT(Math.Min(a.Left, b.Left), Math.Min(a.Top, b.Top), Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom));
    }
}
