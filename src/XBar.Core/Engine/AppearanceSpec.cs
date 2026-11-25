// XBar - GPL-3.0-or-later
using System.Globalization;
using System.Text;
using XBar.Core.Config;

namespace XBar.Core.Engine;

/// <summary>Everything the Explorer-side DLL needs to paint one taskbar.</summary>
public sealed record AppearanceSpec(
    IntPtr Taskbar,
    Appearance Look,
    StyleSettings Decor,
    IReadOnlyList<Pill> Pills,
    double Scale,
    double FloatOffsetPx,
    int AnimationMs)
{
    public string Serialize()
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        void Kv(string k, object v) => sb.Append(k).Append('=').Append(Convert.ToString(v, ci)).Append(';');
        Kv("hwnd", Taskbar.ToInt64());
        Kv("accent", Look.Accent.ToString().ToLowerInvariant());
        Kv("color", Look.Color);
        Kv("blur", Look.BlurRadius);
        Kv("line", Look.ShowLine ? 1 : 0);
        Kv("border", Decor.BorderEnabled ? 1 : 0);
        Kv("bordercolor", Decor.BorderColor);
        Kv("borderw", Decor.BorderThickness * Scale);
        Kv("glow", Decor.GlowEnabled ? 1 : 0);
        Kv("shadow", Decor.ShadowEnabled ? 1 : 0);
        Kv("shadowop", Decor.ShadowOpacity);
        Kv("shadowblur", Decor.ShadowBlur * Scale);
        Kv("grad", Decor.GradientEnabled ? 1 : 0);
        Kv("gradcolor", Decor.GradientColor);
        Kv("gradangle", Decor.GradientAngle);
        Kv("float", FloatOffsetPx);
        Kv("anim", AnimationMs);
        Kv("scale", Scale);
        sb.Append("pills=");
        sb.Append(string.Join('|', Pills.Select(p => $"{p.Rect.Left},{p.Rect.Top},{p.Rect.Right},{p.Rect.Bottom},{p.Radius}")));
        sb.Append(';');
        return sb.ToString();
    }
}

/// <summary>Receives appearance updates. The real implementation talks to XBar.Tap.dll.</summary>
public interface IAppearanceSink
{
    bool IsAvailable { get; }
    string Status { get; }
    void Apply(AppearanceSpec spec);
    void SetOpacity(IntPtr taskbar, double opacity, int ms);
    void RestoreAll();
    void OnTaskbarRecreated();
}

public sealed class NullAppearanceSink : IAppearanceSink
{
    public bool IsAvailable => false;
    public string Status => "Appearance module not loaded";
    public void Apply(AppearanceSpec spec) { }
    public void SetOpacity(IntPtr taskbar, double opacity, int ms) { }
    public void RestoreAll() { }
    public void OnTaskbarRecreated() { }
}
