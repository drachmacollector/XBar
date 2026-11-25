// XBar - GPL-3.0-or-later
using XBar.Core.Config;
using XBar.Core.Engine;
using XBar.Core.Native;

namespace XBar.Core.Tests;

public class PillLayoutTests
{
    // Real geometry captured from this machine (1920x1080 @125%, centred taskbar).
    static readonly RECT Tb = new(0, 1020, 1920, 1080);
    static readonly RECT Apps = new(738, 1020, 1208, 1080);
    static readonly RECT Tray = new(1525, 1020, 1900, 1080);

    [Fact]
    public void Split_produces_apps_and_tray_pills_relative_to_window()
    {
        var s = new ShapeSettings();
        var pills = PillLayout.Compute(new TaskbarGeometry(Tb, Apps, Tray), s, 1.25, true, false);
        Assert.Equal(2, pills.Count);
        var apps = pills[0];
        Assert.Equal("apps", apps.Kind);
        Assert.Equal(738 - 8, apps.Rect.Left);   // padding 6 DIP * 1.25 = 7.5 -> 8
        Assert.Equal(1208 + 8, apps.Rect.Right);
        Assert.Equal(5, apps.Rect.Top);           // margin 4 * 1.25
        Assert.Equal(60 - 5, apps.Rect.Bottom);
        Assert.True(apps.Radius <= apps.Rect.Height / 2);

        var tray = pills[1];
        Assert.Equal("tray", tray.Kind);
        Assert.True(tray.Rect.Right <= 1920 - 5); // keeps a gap from the screen edge
        Assert.True(tray.Rect.Left > apps.Rect.Right);
    }

    [Fact]
    public void Tray_hidden_when_not_included()
    {
        var pills = PillLayout.Compute(new TaskbarGeometry(Tb, Apps, Tray), new ShapeSettings(), 1.0, false, false);
        Assert.Single(pills);
    }

    [Fact]
    public void Single_mode_spans_with_side_margins()
    {
        var s = new ShapeSettings { Mode = ShapeMode.Single, SingleMarginX = 10 };
        var pills = PillLayout.Compute(new TaskbarGeometry(Tb, Apps, Tray), s, 1.0, true, false);
        var p = Assert.Single(pills);
        Assert.Equal(10, p.Rect.Left);
        Assert.Equal(1910, p.Rect.Right);
    }

    [Fact]
    public void Fill_mode_is_full_rect_without_radius()
    {
        var pills = PillLayout.Compute(new TaskbarGeometry(Tb, Apps, Tray), new ShapeSettings(), 1.0, true, true);
        var p = Assert.Single(pills);
        Assert.Equal(new RECT(0, 0, 1920, 60), p.Rect);
        Assert.Equal(0, p.Radius);
    }

    [Fact]
    public void Radius_is_clamped_to_half_height()
    {
        var s = new ShapeSettings();
        s.Apps.CornerRadius = 999;
        var pills = PillLayout.Compute(new TaskbarGeometry(Tb, Apps, default), s, 1.0, false, false);
        Assert.Equal(pills[0].Rect.Height / 2, pills[0].Radius);
    }

    [Fact]
    public void No_content_means_no_split_pills()
    {
        var pills = PillLayout.Compute(new TaskbarGeometry(Tb, default, default), new ShapeSettings(), 1.0, true, false);
        Assert.Empty(pills);
    }
}

public class ParsingTests
{
    [Theory]
    [InlineData("#80102030", 0x80, 0x10, 0x20, 0x30)]
    [InlineData("#102030", 0xFF, 0x10, 0x20, 0x30)]
    [InlineData("8F00", 0x88, 0xFF, 0x00, 0x00)]
    public void Colors_parse(string s, int a, int r, int g, int b)
    {
        Assert.Equal(((byte)a, (byte)r, (byte)g, (byte)b), ColorText.Parse(s));
    }

    [Fact]
    public void Bad_color_falls_back() => Assert.Equal(((byte)1, (byte)2, (byte)3, (byte)4), ColorText.Parse("nope", (1, 2, 3, 4)));

    [Fact]
    public void Hotkey_parses_modifiers_and_key()
    {
        var hk = Hotkey.Parse("Ctrl+Win+Alt+R");
        Assert.Equal(Win32.MOD_CONTROL | Win32.MOD_WIN | Win32.MOD_ALT, hk.Modifiers);
        Assert.Equal((uint)'R', hk.Vk);
        Assert.Equal((uint)0x20, Hotkey.Parse("Ctrl+Space").Vk);
    }

    [Fact]
    public void Settings_roundtrip_through_json()
    {
        var s = new Settings();
        s.Rules.Add(new AppRule { Pattern = "EXCEL.EXE" });
        s.Style.Desktop.Accent = AccentMode.Blur;
        var back = SettingsStore.Import(SettingsStore.Export(s));
        Assert.Equal("EXCEL.EXE", back.Rules[0].Pattern);
        Assert.Equal(AccentMode.Blur, back.Style.Desktop.Accent);
    }

    [Fact]
    public void Appearance_spec_serializes_pills()
    {
        var spec = new AppearanceSpec(new IntPtr(42), new Appearance(), new StyleSettings(),
            new[] { new Pill(new RECT(1, 2, 3, 4), 5, "apps") }, 1.25, 0, 180);
        var text = spec.Serialize();
        Assert.Contains("hwnd=42;", text);
        Assert.Contains("pills=1,2,3,4,5;", text);
        Assert.Contains("accent=acrylic;", text);
    }
}

public class RuleTests
{
    static WindowFacts W(string proc, string cls = "C", string title = "T") => new(IntPtr.Zero, cls, title, proc, default, true, "D");

    [Fact]
    public void Process_rule_ignores_extension_and_case() =>
        Assert.True(WindowTracker.Matches(new AppRule { Pattern = "excel" }, W("EXCEL.EXE")));

    [Fact]
    public void Disabled_rule_never_matches() =>
        Assert.False(WindowTracker.Matches(new AppRule { Pattern = "excel", Enabled = false }, W("EXCEL.EXE")));

    [Fact]
    public void Title_rule_matches_substring() =>
        Assert.True(WindowTracker.Matches(new AppRule { Match = RuleMatch.TitleContains, Pattern = "youtube" }, W("chrome.exe", title: "Video - YouTube")));
}
