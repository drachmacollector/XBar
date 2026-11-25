// XBar - GPL-3.0-or-later
using XBar.Core.Native;

namespace XBar.Core.Engine;

/// <summary>
/// Clips Explorer's taskbar window to the pill shapes with SetWindowRgn.
/// Pixels outside the region are not part of the window, so clicks there
/// fall through to the app underneath. That is what makes the overlay usable.
/// </summary>
public sealed class ShapeController
{
    readonly Dictionary<IntPtr, string> _applied = new();

    public void Apply(IntPtr taskbar, IReadOnlyList<Pill> pills)
    {
        string key = string.Join("|", pills.Select(p => $"{p.Rect}{p.Radius}"));
        if (_applied.TryGetValue(taskbar, out var prev) && prev == key && Win32.GetWindowRgnBox(taskbar, out _) != 0)
            return;

        MarkJournal();
        IntPtr rgn = Win32.CreateRectRgn(0, 0, 0, 0);
        foreach (var p in pills)
        {
            var r = p.Rect;
            IntPtr piece = p.Radius > 0
                ? Win32.CreateRoundRectRgn(r.Left, r.Top, r.Right + 1, r.Bottom + 1, p.Radius * 2, p.Radius * 2)
                : Win32.CreateRectRgn(r.Left, r.Top, r.Right, r.Bottom);
            Win32.CombineRgn(rgn, rgn, piece, Win32.RGN_OR);
            Win32.DeleteObject(piece);
        }

        // On success the system owns the region handle; we must not delete it.
        if (Win32.SetWindowRgn(taskbar, rgn, true) == 0)
        {
            Win32.DeleteObject(rgn);
            Log.Warn($"SetWindowRgn failed for {taskbar}");
            return;
        }
        _applied[taskbar] = key;
        Log.Debug($"Region {taskbar}: {key}");
    }

    /// <summary>Makes the bar fully invisible and click-through (fullscreen / peek).</summary>
    public void Hide(IntPtr taskbar)
    {
        if (_applied.TryGetValue(taskbar, out var prev) && prev == "hidden") return;
        MarkJournal();
        IntPtr rgn = Win32.CreateRectRgn(0, 0, 0, 0);
        if (Win32.SetWindowRgn(taskbar, rgn, true) == 0) { Win32.DeleteObject(rgn); return; }
        _applied[taskbar] = "hidden";
    }

    public void Clear(IntPtr taskbar)
    {
        Win32.SetWindowRgn(taskbar, IntPtr.Zero, true);
        _applied.Remove(taskbar);
    }

    public void ClearAll()
    {
        foreach (var h in _applied.Keys.ToList()) Clear(h);
        RestoreService.RestoreRegions();
        _applied.Clear();
        SafetyJournal.Update(j => j.RegionChanged = false);
    }

    public void Forget() => _applied.Clear();

    bool _journaled;
    void MarkJournal()
    {
        if (_journaled) return;
        SafetyJournal.Update(j => { j.Dirty = true; j.RegionChanged = true; });
        _journaled = true;
    }

    public void ResetJournalFlag() => _journaled = false;
}
