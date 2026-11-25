// XBar - GPL-3.0-or-later
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Controls;
using XBar.Core;
using XBar.Core.Engine;
using Forms = System.Windows.Forms;
using WpfMenuItem = System.Windows.Controls.MenuItem;

namespace XBar.App;

public sealed class TrayIcon : IDisposable
{
    readonly AppHost _host;
    readonly Forms.NotifyIcon _icon;
    Icon? _active, _paused;

    public TrayIcon(AppHost host)
    {
        _host = host;
        _active = MakeIcon(false);
        _paused = MakeIcon(true);
        _icon = new Forms.NotifyIcon { Icon = _active, Text = "XBar", Visible = true };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) _host.ShowPanel();
            else if (e.Button == Forms.MouseButtons.Right) ShowMenu();
        };
    }

    public void Refresh()
    {
        var eng = _host.Engine;
        bool paused = eng?.Paused ?? true;
        _icon.Icon = paused ? _paused : _active;
        var text = paused ? $"XBar: paused" : $"XBar: {(_host.Store.Current.Overlay.Enabled ? "overlay" : "reserve mode")}";
        _icon.Text = text.Length > 63 ? text[..63] : text;
    }

    public void ShowBalloon(string text)
    {
        try { _icon.ShowBalloonTip(4000, "XBar", text, Forms.ToolTipIcon.None); } catch { }
    }

    void ShowMenu()
    {
        var eng = _host.Engine;
        var s = _host.Store.Current;

        var menu = new ContextMenu();
        menu.Items.Add(Item("Open control panel", _host.ShowPanel, bold: true));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(eng?.Paused == true ? "Resume" : "Pause", () => eng?.TogglePause()));
        menu.Items.Add(Item(s.Overlay.Enabled ? "Switch to reserve mode" : "Switch to overlay mode", () => eng?.ToggleOverlay()));

        if (s.Profiles.Count > 0)
        {
            var profiles = new WpfMenuItem { Header = "Profiles" };
            foreach (var p in s.Profiles)
            {
                var pi = Item(p.Name, () => _host.ApplyProfile(p));
                pi.IsChecked = p.Name == s.ActiveProfile;
                profiles.Items.Add(pi);
            }
            menu.Items.Add(profiles);
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Restore taskbar now", () => eng?.Panic()));
        menu.Items.Add(Item("Open log folder", Log.OpenFolder));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Exit XBar", _host.Exit));

        // A WPF context menu needs an active window to dismiss properly when clicking elsewhere.
        var anchor = new Window
        {
            Width = 0, Height = 0, WindowStyle = WindowStyle.None, ShowInTaskbar = false,
            AllowsTransparency = true, Background = System.Windows.Media.Brushes.Transparent, Topmost = true,
            Left = -10000, Top = -10000,
        };
        anchor.Show();
        anchor.Activate();
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.Closed += (_, _) => anchor.Close();
        menu.IsOpen = true;
    }

    static WpfMenuItem Item(string text, Action action, bool bold = false)
    {
        var mi = new WpfMenuItem { Header = text, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
        mi.Click += (_, _) =>
        {
            try { action(); } catch (Exception ex) { Log.Error($"Menu '{text}'", ex); }
        };
        return mi;
    }

    /// <summary>Draws the tray icon: two pills (apps + tray) on a dark tile.</summary>
    static Icon MakeIcon(bool paused)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            var accent = paused ? System.Drawing.Color.FromArgb(150, 150, 150) : System.Drawing.Color.FromArgb(96, 165, 250);
            using var bar = RoundRect(new RectangleF(3, 18, 18, 10), 5);
            using var tray = RoundRect(new RectangleF(23, 18, 7, 10), 4);
            using var fill = new SolidBrush(accent);
            using var fill2 = new SolidBrush(System.Drawing.Color.FromArgb(paused ? 120 : 200, accent));
            g.FillPath(fill, bar);
            g.FillPath(fill2, tray);
            using var pen = new Pen(System.Drawing.Color.FromArgb(paused ? 150 : 235, 255, 255, 255), 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            // stylised "X"
            g.DrawLine(pen, 9, 4, 19, 14);
            g.DrawLine(pen, 19, 4, 9, 14);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    static GraphicsPath RoundRect(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _active?.Dispose();
        _paused?.Dispose();
    }
}
