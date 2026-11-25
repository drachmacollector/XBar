// XBar - GPL-3.0-or-later
using Microsoft.Win32;
using XBar.Core;

namespace XBar.App;

public static class Shortcuts
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "XBar";

    static string ExePath => Environment.ProcessPath!;

    // ---- run at login (HKCU, no admin) ----
    public static bool IsRunAtLogin()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(RunValue) is string v && v.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
    }

    public static void SetRunAtLogin(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue(RunValue, $"\"{ExePath}\" --startup");
        else k.DeleteValue(RunValue, false);
        Log.Info($"Run at login: {on}");
    }

    // ---- Start menu: "XBar" and the emergency "XBar - Restore taskbar" ----
    public static void EnsureStartMenu()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "XBar");
            Directory.CreateDirectory(dir);
            CreateLink(Path.Combine(dir, "XBar.lnk"), ExePath, "", "XBar control panel");
            CreateLink(Path.Combine(dir, "XBar - Restore taskbar.lnk"), ExePath, "--restore", "Put the taskbar back to normal right now");
            CreateLink(Path.Combine(dir, "XBar - Safe mode.lnk"), ExePath, "--safe", "Start XBar without applying any changes");
        }
        catch (Exception ex) { Log.Warn($"Start menu shortcuts: {ex.Message}"); }
    }

    static void CreateLink(string lnk, string target, string args, string description)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return;
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic link = shell.CreateShortcut(lnk);
        link.TargetPath = target;
        link.Arguments = args;
        link.Description = description;
        link.WorkingDirectory = Path.GetDirectoryName(target);
        link.IconLocation = target + ",0";
        link.Save();
    }
}
