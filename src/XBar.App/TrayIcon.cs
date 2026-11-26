// XBar - GPL-3.0-or-later
using System.Drawing;
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
        _active = LoadIcon("xbar.ico");
        _paused = LoadIcon("xbar-paused.ico");
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
        menu.Items.Add(Item("Open control panel", _host.ShowPanel, bold: true, glyph: ""));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(eng?.Paused == true ? "Resume" : "Pause", () => eng?.TogglePause(), glyph: eng?.Paused == true ? "" : ""));
        menu.Items.Add(Item(s.Overlay.Enabled ? "Switch to reserve mode" : "Switch to overlay mode", () => eng?.ToggleOverlay(), glyph: ""));

        if (s.Profiles.Count > 0)
        {
            var profiles = new WpfMenuItem { Header = "Profiles", Icon = GlyphIcon("") };
            foreach (var p in s.Profiles)
            {
                var pi = Item(p.Name, () => _host.ApplyProfile(p));
                pi.IsChecked = p.Name == s.ActiveProfile;
                profiles.Items.Add(pi);
            }
            menu.Items.Add(profiles);
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Restore taskbar now", () => eng?.Panic(), glyph: ""));
        menu.Items.Add(Item("Open log folder", Log.OpenFolder, glyph: ""));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Exit XBar", _host.Exit, glyph: ""));

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

    static TextBlock GlyphIcon(string glyph) => new()
    {
        Text = glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    static WpfMenuItem Item(string text, Action action, bool bold = false, string? glyph = null)
    {
        var mi = new WpfMenuItem { Header = text, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
        if (glyph is not null) mi.Icon = GlyphIcon(glyph);
        mi.Click += (_, _) =>
        {
            try { action(); } catch (Exception ex) { Log.Error($"Menu '{text}'", ex); }
        };
        return mi;
    }

    /// <summary>Loads an embedded .ico at the size the tray wants (picks the sharpest frame for the DPI).</summary>
    static Icon LoadIcon(string name)
    {
        var info = System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{name}"));
        using var stream = info.Stream;
        return new Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _active?.Dispose();
        _paused?.Dispose();
    }
}
