// XBar - GPL-3.0-or-later
using System.Windows.Input;
using XBar.Core.Native;

namespace XBar.Core.Engine;

public readonly record struct Hotkey(uint Modifiers, uint Vk, string Text)
{
    public bool IsValid => Vk != 0;

    public static Hotkey Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return default;
        uint mods = 0, vk = 0;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= Win32.MOD_CONTROL; break;
                case "alt": mods |= Win32.MOD_ALT; break;
                case "shift": mods |= Win32.MOD_SHIFT; break;
                case "win": case "windows": mods |= Win32.MOD_WIN; break;
                default:
                    var keyName = raw.Length == 1 && char.IsDigit(raw[0]) ? "D" + raw : raw;
                    if (Enum.TryParse<Key>(keyName, true, out var key))
                        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                    break;
            }
        }
        return new Hotkey(mods, vk, text);
    }

    /// <summary>True while every key of the chord is still physically held.</summary>
    public bool IsHeld()
    {
        static bool Down(int vk) => (Win32.GetAsyncKeyState(vk) & 0x8000) != 0;
        if (!Down((int)Vk)) return false;
        if ((Modifiers & Win32.MOD_CONTROL) != 0 && !Down(0x11)) return false;
        if ((Modifiers & Win32.MOD_ALT) != 0 && !Down(0x12)) return false;
        if ((Modifiers & Win32.MOD_SHIFT) != 0 && !Down(0x10)) return false;
        if ((Modifiers & Win32.MOD_WIN) != 0 && !Down(0x5B) && !Down(0x5C)) return false;
        return true;
    }
}

/// <summary>Registers global hotkeys on a host window.</summary>
public sealed class HotkeyManager : IDisposable
{
    readonly IntPtr _hwnd;
    readonly Dictionary<int, Action> _actions = new();
    int _nextId = 0xB000;

    public HotkeyManager(IntPtr hwnd) { _hwnd = hwnd; }

    public List<string> Failed { get; } = new();

    public void Register(Hotkey hk, Action action)
    {
        if (!hk.IsValid) return;
        int id = _nextId++;
        if (Win32.RegisterHotKey(_hwnd, id, hk.Modifiers | Win32.MOD_NOREPEAT, hk.Vk))
            _actions[id] = action;
        else
        {
            Failed.Add(hk.Text);
            Log.Warn($"Hotkey {hk.Text} could not be registered (in use by another app?)");
        }
    }

    public bool Handle(int msg, IntPtr wParam)
    {
        if (msg != Win32.WM_HOTKEY) return false;
        if (_actions.TryGetValue(wParam.ToInt32(), out var a))
        {
            try { a(); } catch (Exception ex) { Log.Error("Hotkey action failed", ex); }
            return true;
        }
        return false;
    }

    public void Clear()
    {
        foreach (var id in _actions.Keys) Win32.UnregisterHotKey(_hwnd, id);
        _actions.Clear();
        Failed.Clear();
    }

    public void Dispose() => Clear();
}
