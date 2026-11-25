// XBar - GPL-3.0-or-later
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using XBar.Core.Native;

namespace XBar.Core.Engine;

public sealed class MonitorRecord
{
    public string Device { get; set; } = "";
    public RECT Bounds { get; set; }
    public RECT OriginalWork { get; set; }
}

/// <summary>
/// Write-ahead record of every volatile change XBar makes, so any process
/// (the app, the watchdog, a later launch, or --restore) can undo it.
/// </summary>
public sealed class JournalState
{
    public int Version { get; set; } = 1;
    public bool Dirty { get; set; }
    public int Pid { get; set; }
    public long ProcessStartTicks { get; set; }
    public uint ExplorerPid { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public List<MonitorRecord> Monitors { get; set; } = new();
    public bool WorkAreaChanged { get; set; }
    public bool RegionChanged { get; set; }
    public bool TapActive { get; set; }
}

public static class SafetyJournal
{
    static readonly object Gate = new();
    static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static JournalState? Read()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(Paths.JournalFile)) return null;
                return JsonSerializer.Deserialize<JournalState>(File.ReadAllText(Paths.JournalFile), Opts);
            }
            catch (Exception ex)
            {
                Log.Warn($"Journal unreadable, treating as dirty: {ex.Message}");
                return new JournalState { Dirty = true };
            }
        }
    }

    public static void Write(JournalState s)
    {
        lock (Gate)
        {
            s.UpdatedUtc = DateTime.UtcNow;
            var tmp = Paths.JournalFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(s, Opts));
            File.Move(tmp, Paths.JournalFile, overwrite: true);
        }
    }

    public static void Update(Action<JournalState> change)
    {
        lock (Gate)
        {
            var s = Read() ?? new JournalState();
            if (s.Pid != Environment.ProcessId)
            {
                // Owned by a dead or previous run: start a fresh record for this process.
                s = new JournalState();
            }
            s.Pid = Environment.ProcessId;
            s.ProcessStartTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
            s.ExplorerPid = Displays.ExplorerPid();
            change(s);
            Write(s);
        }
    }

    public static void MarkClean()
    {
        lock (Gate)
        {
            var s = Read() ?? new JournalState();
            s.Dirty = false;
            s.WorkAreaChanged = false;
            s.RegionChanged = false;
            s.TapActive = false;
            Write(s);
        }
    }

    /// <summary>True if the journal says changes are applied and its owner is not running.</summary>
    public static bool NeedsRecovery(JournalState? s)
    {
        if (s is null || !s.Dirty) return false;
        return !IsOwnerAlive(s);
    }

    public static bool IsOwnerAlive(JournalState s)
    {
        if (s.Pid == 0 || s.Pid == Environment.ProcessId) return s.Pid == Environment.ProcessId;
        try
        {
            using var p = Process.GetProcessById(s.Pid);
            return p.StartTime.ToUniversalTime().Ticks == s.ProcessStartTicks && !p.HasExited;
        }
        catch { return false; }
    }
}
