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
    /// <param name="liftPx">Gap kept under the pills (float). Pills are centred in the strip above it,
    /// so the content only has to move up by half of this to stay centred inside them.</param>
    public static List<Pill> Compute(TaskbarGeometry g, ShapeSettings s, double scale, bool includeTray, bool fillEdgeToEdge, int liftPx = 0)
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
            var (top, bottom) = Vertical(p, h, scale, liftPx);
            int mx = Px(s.SingleMarginX, scale);
            var r = new RECT(mx, top, w - mx, bottom);
            if (!r.IsEmpty) pills.Add(new Pill(r, Radius(p.CornerRadius, scale, r), "single"));
            return pills;
        }

        // Split: apps pill + tray pill
        bool hasTray = includeTray && g.HasTray;

        RECT appsRect = default, trayRect = default;
        var ap = s.Apps;
        var tp = s.Tray;

        var (aTop, aBottom) = Vertical(ap, h, scale, liftPx);
        var (tTop, tBottom) = Vertical(tp, h, scale, liftPx);

        if (g.HasApps)
        {
            int pad = Px(ap.PaddingX, scale);
            // Padding is applied in full on both sides; the pill may extend past the screen
            // edge (the window region clips it) rather than silently losing padding on one side.
            appsRect = new RECT(
                g.Apps.Left - tb.Left - pad,
                aTop,
                g.Apps.Right - tb.Left + pad,
                aBottom);
        }

        if (hasTray)
        {
            int pad = Px(tp.PaddingX, scale);
            // Tray pill is attached to the screen edge: no right padding, so the Show Desktop
            // sliver at the far right stays inside the pill.
            trayRect = new RECT(
                g.Tray.Left - tb.Left - pad,
                tTop,
                w,
                tBottom);
        }

        // Where the pills would overlap, split the difference so both keep a small gap.
        if (g.HasApps && hasTray && trayRect.Left < appsRect.Right + Px(GapDips, scale))
        {
            int gap = Px(GapDips, scale);
            int mid = (Math.Max(trayRect.Left, appsRect.Left) + Math.Min(appsRect.Right, trayRect.Right)) / 2;
            appsRect.Right = mid - gap / 2;
            trayRect.Left = mid + (gap - gap / 2);
        }

        if (g.HasApps)
        {
            var r = appsRect;
            r.Left = Math.Max(0, r.Left);   // clip to the window only after the overlap resolution
            r.Right = Math.Min(w, r.Right);
            if (!r.IsEmpty) pills.Add(new Pill(r, Radius(ap.CornerRadius, scale, r), "apps"));
        }

        if (hasTray)
        {
            var r = trayRect;
            r.Left = Math.Max(0, r.Left);
            if (!r.IsEmpty) pills.Add(new Pill(r, Radius(tp.CornerRadius, scale, r), "tray"));
        }

        return pills;
    }

    const double GapDips = 4;

    /// <summary>Vertical extent of a pill: its height, capped to the strip above the float gap, centred in it.</summary>
    static (int Top, int Bottom) Vertical(PillSettings p, int taskbarHeight, double scale, int liftPx)
    {
        int strip = Math.Max(0, taskbarHeight - Math.Max(0, liftPx));
        int height = Math.Clamp(Px(p.Height, scale), 0, strip);
        int top = (strip - height) / 2;
        return (top, top + height);
    }

    static int Px(double dips, double scale) => (int)Math.Round(dips * scale);

    static int Radius(double dips, double scale, RECT r) =>
        Math.Clamp((int)Math.Round(dips * scale), 0, Math.Min(r.Width, r.Height) / 2);
}
