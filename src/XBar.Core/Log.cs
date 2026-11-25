// XBar - GPL-3.0-or-later
using System.Collections.Concurrent;
using System.Diagnostics;

namespace XBar.Core;

public static class Paths
{
    public static string DataDir { get; } = Init();
    public static string LogDir => Path.Combine(DataDir, "logs");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string JournalFile => Path.Combine(DataDir, "state.json");
    public static string TapDir => Path.Combine(DataDir, "tap");

    static string Init()
    {
        var dir = Environment.GetEnvironmentVariable("XBAR_DATA_DIR");
        if (string.IsNullOrWhiteSpace(dir))
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XBar");
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "logs"));
        return dir;
    }
}

/// <summary>Tiny thread-safe file logger. One file per day per role, 7 days kept.</summary>
public static class Log
{
    static readonly object Gate = new();
    static string _role = "app";
    static readonly ConcurrentQueue<string> Recent = new();

    public static event Action<string>? LineWritten;

    public static void Init(string role)
    {
        _role = role;
        try
        {
            foreach (var f in Directory.GetFiles(Paths.LogDir, "*.log"))
                if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-7)) File.Delete(f);
        }
        catch { }
    }

    public static IReadOnlyCollection<string> RecentLines => Recent.ToArray();

    public static void Info(string msg) => Write("INF", msg);
    public static void Warn(string msg) => Write("WRN", msg);
    public static void Error(string msg, Exception? ex = null) => Write("ERR", ex is null ? msg : $"{msg}: {ex}");
    public static void Debug(string msg) => Write("DBG", msg);

    static void Write(string level, string msg)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {level} [{Environment.CurrentManagedThreadId}] {msg}";
        System.Diagnostics.Debug.WriteLine(line);
        Recent.Enqueue(line);
        while (Recent.Count > 500 && Recent.TryDequeue(out _)) { }
        try
        {
            lock (Gate)
                File.AppendAllText(Path.Combine(Paths.LogDir, $"{_role}-{DateTime.Now:yyyy-MM-dd}.log"), line + Environment.NewLine);
        }
        catch { /* logging must never throw */ }
        try { LineWritten?.Invoke(line); } catch { }
    }

    public static void OpenFolder()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", Paths.LogDir) { UseShellExecute = true }); } catch { }
    }
}
