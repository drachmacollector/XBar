// XBar - GPL-3.0-or-later
using XBar.Core.Config;

namespace XBar.App.UI;

public sealed record Preset(string Name, Appearance Look, Appearance Maximized, string? Border, double Radius, bool Shadow, bool Glow = false, bool Accent = false)
{
    public void ApplyTo(Settings s)
    {
        s.Style.Desktop = Look.Clone();
        s.Style.Desktop.Enabled = true;
        s.Style.MaximizedWindow = Maximized.Clone();
        s.Style.BorderEnabled = Border is not null;
        if (Border is not null) s.Style.BorderColor = Border;
        s.Style.ShadowEnabled = Shadow;
        s.Style.GlowEnabled = Glow;
        s.Style.SyncAccentColor = Accent;
        s.Shape.Apps.CornerRadius = Radius;
        s.Shape.Tray.CornerRadius = Radius;
    }
}

public static class Presets
{
    static Appearance A(AccentMode m, string c, double blur = 20, bool enabled = true) =>
        new() { Accent = m, Color = c, BlurRadius = blur, Enabled = enabled };

    public static IReadOnlyList<Preset> All { get; } = new[]
    {
        new Preset("Liquid Glass", A(AccentMode.Liquid, "#1C14181E", 0), A(AccentMode.Liquid, "#55181C22", 6), "#80FFFFFF", 30, true),
        new Preset("Original", A(AccentMode.Acrylic, "#30101010"), A(AccentMode.Acrylic, "#90101010"), "#33FFFFFF", 12, true),
        new Preset("Glass", A(AccentMode.Acrylic, "#30101010"), A(AccentMode.Acrylic, "#90101010"), "#33FFFFFF", 12, true),
        new Preset("Clear", A(AccentMode.Clear, "#00000000"), A(AccentMode.Clear, "#60101010"), "#22FFFFFF", 12, false),
        new Preset("Frost", A(AccentMode.Blur, "#20FFFFFF", 30), A(AccentMode.Blur, "#60202020", 30), "#40FFFFFF", 14, true),
        new Preset("Midnight", A(AccentMode.Opaque, "#FF14141C"), A(AccentMode.Opaque, "#FF14141C", enabled: false), "#1FFFFFFF", 10, true),
        new Preset("Light", A(AccentMode.Acrylic, "#90F3F3F3"), A(AccentMode.Acrylic, "#D0F3F3F3"), "#30000000", 12, true),
        new Preset("Accent", A(AccentMode.Acrylic, "#700078D4"), A(AccentMode.Acrylic, "#B00078D4"), "#40FFFFFF", 12, true, Accent: true),
        new Preset("Neon", A(AccentMode.Clear, "#20000000"), A(AccentMode.Acrylic, "#A0101010"), "#FF3BA8F0", 16, false, Glow: true),
        new Preset("Square", A(AccentMode.Acrylic, "#50101010"), A(AccentMode.Acrylic, "#A0101010"), null, 2, false),
    };
}
