// XBar - GPL-3.0-or-later
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using XBar.Core.Config;
using XBar.Core.Engine;

namespace XBar.App.UI;

public static class EnumLists
{
    public static Array AccentModes { get; } = Enum.GetValues<AccentMode>();
    public static Array ShapeModes { get; } = Enum.GetValues<ShapeMode>();
    public static Array RuleMatches { get; } = Enum.GetValues<RuleMatch>();
    public static Array RuleActions { get; } = Enum.GetValues<RuleAction>();
}

/// <summary>"#AARRGGBB" string &lt;-&gt; Brush.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public static HexToBrushConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var (a, r, g, b) = ColorText.Parse(value as string, (0, 0, 0, 0));
        var br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        br.Freeze();
        return br;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public static BoolToVisibilityConverter Instance { get; } = new();
    public object Convert(object value, Type t, object p, CultureInfo c) => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => value is Visibility.Visible;
}
