// XBar - GPL-3.0-or-later
using Microsoft.Win32;

namespace XBar.Core.Engine;

public static class ThemeInfo
{
    /// <summary>The taskbar follows the *system* theme, not the apps theme.</summary>
    public static bool SystemUsesLightTheme()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("SystemUsesLightTheme") is int v && v != 0;
        }
        catch { return false; }
    }

    public static bool AppsUseLightTheme()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch { return false; }
    }

    /// <summary>Windows accent colour as (R,G,B).</summary>
    public static (byte R, byte G, byte B) AccentColor()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (k?.GetValue("AccentColor") is int abgr)
            {
                uint u = unchecked((uint)abgr);
                return ((byte)(u & 0xFF), (byte)((u >> 8) & 0xFF), (byte)((u >> 16) & 0xFF));
            }
        }
        catch { }
        return (0x00, 0x78, 0xD4);
    }

    public static bool IsTaskbarCentered()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            return k?.GetValue("TaskbarAl") is not int v || v != 0; // Win11 default is centred
        }
        catch { return true; }
    }
}

public static class ColorText
{
    public static (byte A, byte R, byte G, byte B) Parse(string? s, (byte, byte, byte, byte) fallback = default)
    {
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        s = s.Trim().TrimStart('#');
        try
        {
            if (s.Length == 6) s = "FF" + s;
            if (s.Length == 3) s = "F" + s;
            if (s.Length == 4) s = string.Concat(s.Select(c => new string(c, 2)));
            if (s.Length != 8) return fallback;
            uint v = Convert.ToUInt32(s, 16);
            return ((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        }
        catch { return fallback; }
    }

    public static string Format(byte a, byte r, byte g, byte b) => $"#{a:X2}{r:X2}{g:X2}{b:X2}";
}
