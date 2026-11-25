// XBar - GPL-3.0-or-later
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using XBar.Core.Engine;

namespace XBar.App.UI;

/// <summary>Compact colour picker with alpha: swatch button + popup (HSV square, hue, alpha, hex, presets).</summary>
public sealed class ColorField : UserControl
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(string), typeof(ColorField),
        new FrameworkPropertyMetadata("#80000000", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((ColorField)d).SyncFromColor()));

    public static readonly RoutedEvent ColorChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(ColorChanged), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ColorField));

    public string Color { get => (string)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    public event RoutedEventHandler ColorChanged { add => AddHandler(ColorChangedEvent, value); remove => RemoveHandler(ColorChangedEvent, value); }

    static readonly string[] Presets =
    {
        "#00000000", "#30101010", "#80101010", "#E0101018", "#40FFFFFF", "#90F3F3F3",
        "#600078D4", "#80602080", "#80104030", "#80A02040", "#80D07010", "#8020A0C0",
    };

    readonly Border _swatch = new() { Width = 30, Height = 20, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
    readonly TextBlock _hexLabel = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), FontFamily = new FontFamily("Consolas"), MinWidth = 76 };
    readonly Popup _popup = new() { StaysOpen = false, Placement = PlacementMode.Bottom, AllowsTransparency = true };
    readonly Rectangle _svHue = new();
    readonly Canvas _svCanvas = new() { Width = 220, Height = 140, ClipToBounds = true, Background = Brushes.Transparent };
    readonly Ellipse _svMarker = new() { Width = 12, Height = 12, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
    readonly Slider _hue = new() { Minimum = 0, Maximum = 360, Width = 220, Margin = new Thickness(0, 10, 0, 0) };
    readonly Slider _alpha = new() { Minimum = 0, Maximum = 255, Width = 220, Margin = new Thickness(0, 6, 0, 0) };
    readonly TextBox _hexBox = new() { Width = 110, FontFamily = new FontFamily("Consolas") };
    readonly TextBlock _alphaLabel = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };

    double _h, _s, _v;
    byte _a;
    bool _syncing;

    public ColorField()
    {
        var checker = Checker();
        var swatchHost = new Grid { Width = 30, Height = 20 };
        swatchHost.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = checker });
        _swatch.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 128, 128, 128));
        swatchHost.Children.Add(_swatch);

        var button = new Button { Padding = new Thickness(6, 4, 8, 4) };
        var bp = new StackPanel { Orientation = Orientation.Horizontal };
        bp.Children.Add(swatchHost);
        bp.Children.Add(_hexLabel);
        button.Content = bp;
        button.Click += (_, _) => { _popup.PlacementTarget = button; _popup.IsOpen = true; };

        Content = new Grid { Children = { button, _popup } };
        _popup.Child = BuildPopup(checker);
        SyncFromColor();
    }

    UIElement BuildPopup(Brush checker)
    {
        // Saturation/value square
        var white = new Rectangle { Width = 220, Height = 140, Fill = new LinearGradientBrush(Colors.White, System.Windows.Media.Color.FromArgb(0, 255, 255, 255), 0) };
        var black = new Rectangle { Width = 220, Height = 140, Fill = new LinearGradientBrush(System.Windows.Media.Color.FromArgb(0, 0, 0, 0), Colors.Black, 90) };
        _svHue.Width = 220; _svHue.Height = 140;
        _svCanvas.Children.Add(_svHue);
        _svCanvas.Children.Add(white);
        _svCanvas.Children.Add(black);
        _svCanvas.Children.Add(_svMarker);
        _svCanvas.MouseLeftButtonDown += (_, e) => { _svCanvas.CaptureMouse(); PickSv(e.GetPosition(_svCanvas)); };
        _svCanvas.MouseMove += (_, e) => { if (_svCanvas.IsMouseCaptured) PickSv(e.GetPosition(_svCanvas)); };
        _svCanvas.MouseLeftButtonUp += (_, _) => _svCanvas.ReleaseMouseCapture();
        var svBorder = new Border { CornerRadius = new CornerRadius(6), Child = _svCanvas, ClipToBounds = true };

        // Hue slider over a rainbow
        var rainbow = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (int i = 0; i <= 6; i++) rainbow.GradientStops.Add(new GradientStop(FromHsv(i * 60, 1, 1, 255), i / 6.0));
        var hueHost = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        hueHost.Children.Add(new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = rainbow, VerticalAlignment = VerticalAlignment.Center });
        _hue.Margin = new Thickness(0);
        hueHost.Children.Add(_hue);
        _hue.ValueChanged += (_, _) => { if (!_syncing) { _h = _hue.Value; Commit(); } };

        // Alpha slider
        var alphaRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        _alpha.Width = 172; _alpha.Margin = new Thickness(0);
        alphaRow.Children.Add(_alpha);
        alphaRow.Children.Add(_alphaLabel);
        _alpha.ValueChanged += (_, _) => { if (!_syncing) { _a = (byte)_alpha.Value; Commit(); } };

        var alphaCaption = new TextBlock { Text = "Opacity", Margin = new Thickness(0, 8, 0, 0), Opacity = 0.7, FontSize = 12 };
        var hueCaption = new TextBlock { Text = "Hue", Margin = new Thickness(0, 8, 0, -6), Opacity = 0.7, FontSize = 12 };

        // Hex
        var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        hexRow.Children.Add(new TextBlock { Text = "Hex (#AARRGGBB)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Opacity = 0.7, FontSize = 12 });
        hexRow.Children.Add(_hexBox);
        _hexBox.LostKeyboardFocus += (_, _) => ApplyHex();
        _hexBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyHex(); };

        // Presets
        var presets = new WrapPanel { Margin = new Thickness(0, 10, 0, 0), Width = 220 };
        foreach (var p in Presets)
        {
            var (a, r, g, b) = ColorText.Parse(p);
            var cell = new Grid { Width = 28, Height = 22, Margin = new Thickness(0, 0, 4, 4), Cursor = Cursors.Hand, ToolTip = p };
            cell.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = checker });
            cell.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b)), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 128, 128, 128)) });
            cell.MouseLeftButtonUp += (_, _) => { Color = p; RaiseEvent(new RoutedEventArgs(ColorChangedEvent, this)); };
            presets.Children.Add(cell);
        }

        var stack = new StackPanel { Children = { svBorder, hueCaption, hueHost, alphaCaption, alphaRow, hexRow, presets } };
        var card = new Border
        {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 4, 8, 8),
            Child = stack,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, Opacity = 0.3, ShadowDepth = 3 },
        };
        card.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        return card;
    }

    void PickSv(Point p)
    {
        _s = Math.Clamp(p.X / _svCanvas.Width, 0, 1);
        _v = 1 - Math.Clamp(p.Y / _svCanvas.Height, 0, 1);
        Commit();
    }

    void ApplyHex()
    {
        var t = _hexBox.Text.Trim();
        if (!t.StartsWith('#')) t = "#" + t;
        var parsed = ColorText.Parse(t, (0, 0, 0, 0));
        if (parsed == (0, 0, 0, 0) && !t.TrimStart('#').All(c => c == '0')) return;
        Color = ColorText.Format(parsed.Item1, parsed.Item2, parsed.Item3, parsed.Item4);
        RaiseEvent(new RoutedEventArgs(ColorChangedEvent, this));
    }

    void Commit()
    {
        var c = FromHsv(_h, _s, _v, _a);
        _syncing = true;
        try { Color = ColorText.Format(c.A, c.R, c.G, c.B); }
        finally { _syncing = false; }
        UpdateVisuals();
        RaiseEvent(new RoutedEventArgs(ColorChangedEvent, this));
    }

    void SyncFromColor()
    {
        if (_syncing) { UpdateVisuals(); return; }
        var (a, r, g, b) = ColorText.Parse(Color, (0x80, 0, 0, 0));
        _a = a;
        ToHsv(r, g, b, out var h, out _s, out _v);
        if (_s > 0.001) _h = h; // keep hue when grey
        _syncing = true;
        try
        {
            _hue.Value = _h;
            _alpha.Value = _a;
        }
        finally { _syncing = false; }
        UpdateVisuals();
    }

    void UpdateVisuals()
    {
        var (a, r, g, b) = ColorText.Parse(Color, (0x80, 0, 0, 0));
        _swatch.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
        _hexLabel.Text = Color.ToUpperInvariant();
        if (!_hexBox.IsKeyboardFocusWithin) _hexBox.Text = Color.ToUpperInvariant();
        _alphaLabel.Text = $"{Math.Round(a / 255.0 * 100)}%";
        _svHue.Fill = new SolidColorBrush(FromHsv(_h, 1, 1, 255));
        Canvas.SetLeft(_svMarker, _s * _svCanvas.Width - 6);
        Canvas.SetTop(_svMarker, (1 - _v) * _svCanvas.Height - 6);
    }

    static System.Windows.Media.Color FromHsv(double h, double s, double v, byte a)
    {
        h = (h % 360 + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return System.Windows.Media.Color.FromArgb(a, (byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    static void ToHsv(byte rb, byte gb, byte bb, out double h, out double s, out double v)
    {
        double r = rb / 255.0, g = gb / 255.0, b = bb / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        h = d == 0 ? 0 : max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
        s = max == 0 ? 0 : d / max;
        v = max;
    }

    static Brush Checker()
    {
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(System.Windows.Media.Color.FromRgb(204, 204, 204)), null,
            new GeometryGroup { Children = { new RectangleGeometry(new Rect(0, 0, 4, 4)), new RectangleGeometry(new Rect(4, 4, 4, 4)) } }));
        var brush = new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 8, 8), ViewportUnits = BrushMappingMode.Absolute };
        brush.Freeze();
        return brush;
    }
}
