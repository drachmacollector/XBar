// XBar - GPL-3.0-or-later
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace XBar.App.UI;

/// <summary>TextBox that records a key chord ("Ctrl+Win+Alt+R") when you press it.</summary>
public sealed class HotkeyBox : TextBox
{
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey), typeof(string), typeof(HotkeyBox),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((HotkeyBox)d).Text = e.NewValue as string ?? ""));

    public string Hotkey { get => (string)GetValue(HotkeyProperty); set => SetValue(HotkeyProperty, value); }

    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        Width = 200;
        ToolTip = "Click, then press the key combination. Backspace clears it.";
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Back or Key.Delete) { Hotkey = ""; return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        var parts = new List<string>();
        var m = Keyboard.Modifiers;
        if (m.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) parts.Add("Win");
        if (m.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (m.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (parts.Count == 0) return; // require at least one modifier
        parts.Add(key.ToString());
        Hotkey = string.Join("+", parts);
        // Tell the panel something changed.
        RaiseEvent(new TextChangedEventArgs(TextChangedEvent, UndoAction.None));
    }
}
