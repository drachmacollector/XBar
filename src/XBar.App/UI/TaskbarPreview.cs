// XBar - GPL-3.0-or-later
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using XBar.Core.Config;
using XBar.Core.Engine;
using XBar.Core.Native;

namespace XBar.App.UI;

/// <summary>Live mock-up of the bar using the current settings (drawn in WPF, no Explorer needed).</summary>
public sealed class TaskbarPreview : Border
{
    // Mock screen in DIPs; narrower than a real one so the pills read well at preview size.
    const double ScreenW = 960, BarH = 48;
    const int Icons = 7;
    readonly Canvas _canvas = new() { ClipToBounds = true };
    Settings? _settings;
    bool _maximized;

    public TaskbarPreview()
    {
        Height = 150;
        CornerRadius = new CornerRadius(8);
        ClipToBounds = true;
        Child = _canvas;
        SizeChanged += (_, _) => Redraw();
    }

    public void Show(Settings s, bool maximizedState)
    {
        _settings = s;
        _maximized = maximizedState;
        Redraw();
    }

    /// <summary>Liquid-glass chrome for the mock-up: diagonal specular rim + sheen (the real bar does this in the TAP).</summary>
    static void LiquidChrome(Border pill, byte r, byte g, byte b, byte a)
    {
        GradientBrush Rim(double top, double mid, double bottom)
        {
            Color C(double k) => Color.FromArgb((byte)Math.Clamp(a * k, 0, 255), r, g, b);
            return new LinearGradientBrush(new GradientStopCollection
            {
                new(C(top), 0), new(C(mid), 0.30), new(C(mid), 0.70), new(C(bottom), 1),
            }, new Point(0, 0), new Point(1, 1));
        }
        pill.BorderThickness = new Thickness(1.2);
        pill.BorderBrush = Rim(1.0, 0.25, 0.75);
        pill.Child = new Border
        {
            CornerRadius = new CornerRadius(Math.Max(0, pill.CornerRadius.TopLeft - 1)),
            BorderThickness = new Thickness(1),
            BorderBrush = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0x40, 255, 255, 255), 0), new(Color.FromArgb(0x10, 255, 255, 255), 0.4),
                new(Color.FromArgb(0x10, 255, 255, 255), 0.7), new(Color.FromArgb(0x30, 255, 255, 255), 1),
            }, new Point(0, 0), new Point(1, 1)),
            Background = new LinearGradientBrush(Color.FromArgb(0x30, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90),
        };
    }

    void Redraw()
    {
        _canvas.Children.Clear();
        double w = ActualWidth, h = ActualHeight;
        if (_settings is null || w < 50) return;
        double k = w / ScreenW;
        double barTop = h - BarH * k;

        // Wallpaper
        _canvas.Children.Add(new Rectangle
        {
            Width = w, Height = h,
            Fill = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(0x1d, 0x2b, 0x64), 0), new(Color.FromRgb(0x6a, 0x2c, 0x70), 0.55), new(Color.FromRgb(0xf0, 0x80, 0x5a), 1),
                }, new Point(0, 0), new Point(1, 1)),
        });
        // A few "content" shapes so transparency is visible
        for (int i = 0; i < 6; i++)
        {
            var e = new Ellipse { Width = 60 + i * 25, Height = 60 + i * 25, Fill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) };
            Canvas.SetLeft(e, w * (0.1 + i * 0.15)); Canvas.SetTop(e, h * 0.45 - i * 6);
            _canvas.Children.Add(e);
        }

        if (_maximized)
        {
            // A maximised window reaching the bottom edge (the overlay)
            var win = new Border
            {
                Width = w, Height = h, Background = new SolidColorBrush(Color.FromRgb(0xf3, 0xf3, 0xf3)),
                Child = new StackPanel
                {
                    Margin = new Thickness(12, 8, 12, 0),
                    Children =
                    {
                        new Rectangle { Height = 8, Width = w * 0.3, HorizontalAlignment = HorizontalAlignment.Left, Fill = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)), RadiusX = 3, RadiusY = 3 },
                        new Rectangle { Height = 6, Margin = new Thickness(0, 12, 0, 0), Width = w * 0.8, HorizontalAlignment = HorizontalAlignment.Left, Fill = new SolidColorBrush(Color.FromRgb(0xdd, 0xdd, 0xdd)) },
                        new Rectangle { Height = 6, Margin = new Thickness(0, 8, 0, 0), Width = w * 0.7, HorizontalAlignment = HorizontalAlignment.Left, Fill = new SolidColorBrush(Color.FromRgb(0xdd, 0xdd, 0xdd)) },
                        new Rectangle { Height = 6, Margin = new Thickness(0, 8, 0, 0), Width = w * 0.75, HorizontalAlignment = HorizontalAlignment.Left, Fill = new SolidColorBrush(Color.FromRgb(0xdd, 0xdd, 0xdd)) },
                        new Rectangle { Height = 6, Margin = new Thickness(0, 8, 0, 0), Width = w * 0.6, HorizontalAlignment = HorizontalAlignment.Left, Fill = new SolidColorBrush(Color.FromRgb(0xdd, 0xdd, 0xdd)) },
                    },
                },
            };
            _canvas.Children.Add(win);
        }

        var s = _settings;
        var look = _maximized && s.Style.MaximizedWindow.Enabled ? s.Style.MaximizedWindow : s.Style.Desktop;
        double scale = k; // DIP -> preview px

        // Mock content geometry in a 1536x48 DIP taskbar
        int appsW = Icons * 44;
        var tbRect = new RECT(0, 0, (int)ScreenW, (int)BarH);
        var apps = new RECT((int)(ScreenW - appsW) / 2, 0, (int)(ScreenW + appsW) / 2, (int)BarH);
        var tray = new RECT((int)ScreenW - 240, 0, (int)ScreenW - 14, (int)BarH);
        int lift = s.Shape.Mode == ShapeMode.Stock ? 0 : (int)Math.Clamp(s.Shape.FloatOffset, 0, BarH - 32);
        var pills = PillLayout.Compute(new TaskbarGeometry(tbRect, apps, tray), s.Shape, 1.0, s.Shape.ShowTray,
            s.Shape.Mode == ShapeMode.Single && s.Shape.FillWhenMaximized && _maximized, lift);

        var (a, r, g, b) = ColorText.Parse(look.Color, (0x80, 0x10, 0x10, 0x10));
        Brush fill = look.Accent switch
        {
            AccentMode.Normal => new SolidColorBrush(Color.FromArgb(0xD8, 0x20, 0x20, 0x20)),
            AccentMode.Opaque => new SolidColorBrush(Color.FromRgb(r, g, b)),
            AccentMode.Clear => new SolidColorBrush(Color.FromArgb(a, r, g, b)),
            AccentMode.Liquid => new SolidColorBrush(Color.FromArgb((byte)Math.Max((int)a, 0x24), r, g, b)),
            _ => new SolidColorBrush(Color.FromArgb((byte)Math.Max((int)a, 0x50), r, g, b)),
        };
        if (s.Style.GradientEnabled && look.Accent != AccentMode.Normal)
        {
            var (a2, r2, g2, b2) = ColorText.Parse(s.Style.GradientColor, (0x40, 0x30, 0x30, 0x60));
            var lg = new LinearGradientBrush(((SolidColorBrush)fill).Color, Color.FromArgb(a2, r2, g2, b2), s.Style.GradientAngle);
            fill = lg;
        }
        var (ba, br, bg, bb) = ColorText.Parse(s.Style.BorderColor, (0x33, 255, 255, 255));

        foreach (var p in pills)
        {
            var rect = new Border
            {
                Width = p.Rect.Width * scale,
                Height = p.Rect.Height * scale,
                CornerRadius = new CornerRadius(p.Radius * scale),
                Background = fill,
                BorderThickness = new Thickness(s.Style.BorderEnabled ? Math.Max(0.5, s.Style.BorderThickness) : 0),
                BorderBrush = new SolidColorBrush(Color.FromArgb(ba, br, bg, bb)),
            };
            if (look.Accent == AccentMode.Liquid)
                LiquidChrome(rect, br, bg, bb, ba);
            else if (look.Accent is AccentMode.Blur or AccentMode.Acrylic)
            {
                // Hint of frost
                rect.Background = fill;
                rect.Child = new Border { CornerRadius = rect.CornerRadius, Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)) };
            }
            if (s.Style.ShadowEnabled)
                rect.Effect = new DropShadowEffect { BlurRadius = s.Style.ShadowBlur, Opacity = s.Style.ShadowOpacity, ShadowDepth = 2, Direction = 270 };
            else if (s.Style.GlowEnabled)
                rect.Effect = new DropShadowEffect { BlurRadius = 14, Opacity = 0.8, ShadowDepth = 0, Color = Color.FromRgb(br, bg, bb) };
            Canvas.SetLeft(rect, p.Rect.Left * scale);
            Canvas.SetTop(rect, barTop + p.Rect.Top * scale);
            _canvas.Children.Add(rect);

            // Fake icons
            if (p.Kind is "apps" or "single" or "full")
            {
                double icon = 22 * scale, startX = apps.Left * scale + 11 * scale;
                for (int i = 0; i < Icons; i++)
                {
                    var ic = new Rectangle
                    {
                        Width = icon, Height = icon, RadiusX = 4 * scale, RadiusY = 4 * scale,
                        Fill = new SolidColorBrush(i == 0 ? Color.FromRgb(0x3b, 0xa8, 0xf0) : Color.FromArgb(220, (byte)(120 + i * 12), (byte)(200 - i * 10), (byte)(140 + i * 9))),
                    };
                    Canvas.SetLeft(ic, startX + i * 44 * scale);
                    Canvas.SetTop(ic, barTop + (BarH - lift - 22) / 2 * scale);
                    _canvas.Children.Add(ic);
                }
            }
            if (p.Kind == "tray")
            {
                var t = new TextBlock { Text = "ENG   ⌔  🔊  80%   12:30", Foreground = Brushes.White, FontSize = Math.Max(8, 12 * scale) };
                Canvas.SetLeft(t, (tray.Left + 16) * scale);
                Canvas.SetTop(t, barTop + (BarH - lift - 16) / 2 * scale);
                _canvas.Children.Add(t);
            }
        }
    }
}
