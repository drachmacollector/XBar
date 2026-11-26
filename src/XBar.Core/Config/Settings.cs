// XBar - GPL-3.0-or-later
using System.Text.Json.Serialization;

namespace XBar.Core.Config;

[JsonConverter(typeof(JsonStringEnumConverter<ShapeMode>))]
public enum ShapeMode { Split, Single, Stock }

[JsonConverter(typeof(JsonStringEnumConverter<AccentMode>))]
public enum AccentMode { Normal, Opaque, Clear, Blur, Acrylic, Liquid }

[JsonConverter(typeof(JsonStringEnumConverter<TaskbarState>))]
public enum TaskbarState { Desktop, VisibleWindow, MaximizedWindow, StartOpen, SearchOpen, TaskViewOpen }

[JsonConverter(typeof(JsonStringEnumConverter<RuleAction>))]
public enum RuleAction { Appearance, ReserveSpace, AlwaysShow, HideBar }

[JsonConverter(typeof(JsonStringEnumConverter<RuleMatch>))]
public enum RuleMatch { ProcessName, WindowClass, TitleContains }

public sealed class Settings
{
    public int Version { get; set; } = 1;
    public OverlaySettings Overlay { get; set; } = new();
    public ShapeSettings Shape { get; set; } = new();
    public StyleSettings Style { get; set; } = new();
    public BehaviorSettings Behavior { get; set; } = new();
    public HotkeySettings Hotkeys { get; set; } = new();
    public List<AppRule> Rules { get; set; } = new();
    public List<Profile> Profiles { get; set; } = new();
    public string? ActiveProfile { get; set; }
    public bool RunAtLogin { get; set; }
    public bool FirstRunDone { get; set; }

    public Settings Clone() => SettingsStore.Clone(this);
}

public sealed class OverlaySettings
{
    /// <summary>Overlay on: windows use the full screen and the bar floats on top. Off: classic reserved strip.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Pixels left reserved at the bottom of the work area (0 = true full screen).</summary>
    public int BottomInsetPx { get; set; } = 0;
    public bool HideOnFullscreen { get; set; } = true;
    /// <summary>Re-assert the taskbar as topmost when something covers it.</summary>
    public bool KeepOnTop { get; set; } = true;
}

public sealed class PillSettings
{
    public double CornerRadius { get; set; } = 12;
    /// <summary>Pill height in DIPs. Capped at what is left of the taskbar after the float gap.</summary>
    public double Height { get; set; } = 44;
    /// <summary>Extra horizontal space around the content.</summary>
    public double PaddingX { get; set; } = 6;
}

public sealed class ShapeSettings
{
    public ShapeMode Mode { get; set; } = ShapeMode.Split;
    public PillSettings Apps { get; set; } = new();
    public PillSettings Tray { get; set; } = new() { PaddingX = 8 };
    /// <summary>Single-bar mode side margins (DIPs).</summary>
    public double SingleMarginX { get; set; } = 8;
    public bool ShowTray { get; set; } = true;
    /// <summary>Lift the pills this many DIPs above the screen edge. Negative (default) = automatic: whatever room the pill height leaves.</summary>
    public double FloatOffset { get; set; } = -1;
    /// <summary>Single-bar mode: become a full edge-to-edge bar while a window is maximised.</summary>
    public bool FillWhenMaximized { get; set; } = false;
}

public sealed class Appearance
{
    public bool Enabled { get; set; } = true;
    public AccentMode Accent { get; set; } = AccentMode.Liquid;
    /// <summary>#AARRGGBB. For Liquid this is the smoky body tint (the reference design uses ~12 % white; a little dark keeps white icons/clock readable on bright wallpapers).</summary>
    public string Color { get; set; } = "#1C14181E";
    public double BlurRadius { get; set; } = 6;
    public bool ShowLine { get; set; } = false;

    public Appearance Clone() => (Appearance)MemberwiseClone();
}

public sealed class StyleSettings
{
    public Appearance Desktop { get; set; } = new() { Accent = AccentMode.Liquid, Color = "#1C14181E", BlurRadius = 6 };
    public Appearance VisibleWindow { get; set; } = new() { Enabled = false };
    public Appearance MaximizedWindow { get; set; } = new() { Enabled = true, Accent = AccentMode.Liquid, Color = "#55181C22", BlurRadius = 6 };
    public Appearance StartOpen { get; set; } = new() { Enabled = false };
    public Appearance SearchOpen { get; set; } = new() { Enabled = false };
    public Appearance TaskViewOpen { get; set; } = new() { Enabled = false };

    // Pill decoration (shared by all states)
    public bool BorderEnabled { get; set; } = true;
    public string BorderColor { get; set; } = "#80FFFFFF";
    public double BorderThickness { get; set; } = 1;
    public bool GlowEnabled { get; set; } = false;
    public bool ShadowEnabled { get; set; } = true;
    public double ShadowOpacity { get; set; } = 0.45;
    public double ShadowBlur { get; set; } = 12;
    public bool GradientEnabled { get; set; } = false;
    public string GradientColor { get; set; } = "#40303060";
    public double GradientAngle { get; set; } = 90;
    /// <summary>Use the Windows accent colour as the tint (keeps the alpha from Color).</summary>
    public bool SyncAccentColor { get; set; } = false;
    /// <summary>Use a separate light-theme appearance set when Windows is in light mode.</summary>
    public bool AutoLightDark { get; set; } = false;
    public StyleSettings? LightVariant { get; set; }

    public Appearance For(TaskbarState s) => s switch
    {
        TaskbarState.VisibleWindow => VisibleWindow,
        TaskbarState.MaximizedWindow => MaximizedWindow,
        TaskbarState.StartOpen => StartOpen,
        TaskbarState.SearchOpen => SearchOpen,
        TaskbarState.TaskViewOpen => TaskViewOpen,
        _ => Desktop,
    };
}

public sealed class BehaviorSettings
{
    public bool IdleDimEnabled { get; set; } = false;
    public double IdleOpacity { get; set; } = 0.55;
    public int IdleDelayMs { get; set; } = 1500;

    public bool PeekEnabled { get; set; } = true;
    public double PeekOpacity { get; set; } = 0.0;

    public bool AnimationsEnabled { get; set; } = true;
    public int AnimationMs { get; set; } = 180;

    /// <summary>Show a segment only while the mouse is over it (RoundedTB "show on hover").</summary>
    public bool TrayOnHoverOnly { get; set; } = false;
}

public sealed class HotkeySettings
{
    public string Panic { get; set; } = "Ctrl+Win+Alt+R";
    public string TogglePause { get; set; } = "Ctrl+Win+Alt+P";
    public string ToggleOverlay { get; set; } = "Ctrl+Win+Alt+O";
    /// <summary>Held: bar becomes see-through and click-through.</summary>
    public string Peek { get; set; } = "Ctrl+Win+Alt+Space";
    public string NextProfile { get; set; } = "Ctrl+Win+Alt+N";
}

public sealed class AppRule
{
    public bool Enabled { get; set; } = true;
    public RuleMatch Match { get; set; } = RuleMatch.ProcessName;
    public string Pattern { get; set; } = "";
    public RuleAction Action { get; set; } = RuleAction.ReserveSpace;
    /// <summary>Only for Action=Appearance.</summary>
    public Appearance? Appearance { get; set; }
    /// <summary>Only while that window is maximised (for ReserveSpace / Appearance).</summary>
    public bool OnlyWhenMaximized { get; set; } = true;
}

public sealed class Profile
{
    public string Name { get; set; } = "Profile";
    public ShapeSettings Shape { get; set; } = new();
    public StyleSettings Style { get; set; } = new();
    public BehaviorSettings Behavior { get; set; } = new();
}
