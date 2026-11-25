// XBar - GPL-3.0-or-later
using XBar.Core.Config;
using XBar.Core.Native;

namespace XBar.Core.Engine;

/// <summary>Content bounds of the taskbar in screen pixels (from UIA or the TAP).</summary>
public readonly record struct TaskbarGeometry(RECT Taskbar, RECT Apps, RECT Tray)
{
    public bool HasApps => !Apps.IsEmpty;
    public bool HasTray => !Tray.IsEmpty;
}

public readonly record struct Pill(RECT Rect, int Radius, string Kind);

/// <summary>
/// Pure geometry: turns content bounds + shape settings into the pill rectangles
/// (relative to the taskbar window). Both pills come from one pass so they always
/// update together.
/// </summary>
public static class PillLayout
{
    public static List<Pill> Compute(TaskbarGeometry g, ShapeSettings s, double scale, bool includeTray, bool fillEdgeToEdge)
    {
        var pills = new List<Pill>();
        var tb = g.Taskbar;
        int w = tb.Width, h = tb.Height;
        if (w <= 0 || h <= 0) return pills;

        if (s.Mode == ShapeMode.Stock)
        {
            pills.Add(new Pill(new RECT(0, 0, w, h), 0, "full"));
            return pills;
        }

        if (fillEdgeToEdge)
        {
            pills.Add(new Pill(new RECT(0, 0, w, h), 0, "full"));
            return pills;
        }

        if (s.Mode == ShapeMode.Single)
        {
            var p = s.Apps;
            int top = Px(p.MarginTop, scale);
            int bottom = h - Px(p.MarginBottom, scale);
            int mx = Px(s.SingleMarginX, scale);
            var r = new RECT(mx, top, w - mx, bottom);
            if (!r.IsEmpty) pills.Add(new Pill(r, Radius(p.CornerRadius, scale, r), "single"));
            return pills;
        }

        // Split: apps pill + tray pill
        if (g.HasApps)
        {
            var p = s.Apps;
            int pad = Px(p.PaddingX, scale);
            var r = new RECT(
                Math.Max(0, g.Apps.Left - tb.Left - pad),
                Px(p.MarginTop, scale),
                Math.Min(w, g.Apps.Right - tb.Left + pad),
                h - Px(p.MarginBottom, scale));
            if (!r.IsEmpty) pills.Add(new Pill(r, Radius(p.CornerRadius, scale, r), "apps"));
        }

        if (includeTray && g.HasTray)
        {
            var p = s.Tray;
            int pad = Px(p.PaddingX, scale);
            int edgeGap = Px(Math.Max(p.MarginBottom, 4), scale);
            var r = new RECT(
                Math.Max(0, g.Tray.Left - tb.Left - pad),
                Px(p.MarginTop, scale),
                Math.Min(w - edgeGap, g.Tray.Right - tb.Left + pad),
                h - Px(p.MarginBottom, scale));

            // Never let the tray pill overlap the apps pill.
            var apps = pills.FirstOrDefault(x => x.Kind == "apps");
            if (apps.Kind is not null && r.Left < apps.Rect.Right + pad)
                r.Left = apps.Rect.Right + pad;

            if (!r.IsEmpty) pills.Add(new Pill(r, Radius(p.CornerRadius, scale, r), "tray"));
        }

        return pills;
    }

    static int Px(double dips, double scale) => (int)Math.Round(dips * scale);

    static int Radius(double dips, double scale, RECT r) =>
        Math.Clamp((int)Math.Round(dips * scale), 0, Math.Min(r.Width, r.Height) / 2);
}
