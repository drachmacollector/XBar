// XBar - GPL-3.0-or-later
using System.Text.Json;

namespace XBar.Core.Config;

public sealed class SettingsStore : IDisposable
{
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    readonly FileSystemWatcher? _watcher;
    DateTime _lastOwnWrite;

    public Settings Current { get; private set; }
    /// <summary>Raised (on a worker thread) when the file was edited by hand.</summary>
    public event Action<Settings>? ExternallyChanged;

    public SettingsStore()
    {
        Current = Load();
        try
        {
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(Paths.SettingsFile)!, Path.GetFileName(Paths.SettingsFile))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            };
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Renamed += OnFileChanged;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) { Log.Warn($"Settings watcher unavailable: {ex.Message}"); }
    }

    public static Settings Load()
    {
        try
        {
            if (File.Exists(Paths.SettingsFile))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(Paths.SettingsFile), JsonOpts) ?? new Settings();
        }
        catch (Exception ex)
        {
            Log.Error("Settings file invalid; using defaults (file kept as .bad)", ex);
            try { File.Copy(Paths.SettingsFile, Paths.SettingsFile + ".bad", true); } catch { }
        }
        return new Settings();
    }

    public void Save(Settings s)
    {
        Current = s;
        _lastOwnWrite = DateTime.UtcNow;
        var tmp = Paths.SettingsFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s, JsonOpts));
        File.Move(tmp, Paths.SettingsFile, overwrite: true);
    }

    void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (DateTime.UtcNow - _lastOwnWrite < TimeSpan.FromSeconds(1)) return;
        Thread.Sleep(150); // let the editor finish writing
        try
        {
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Paths.SettingsFile), JsonOpts);
            if (s is null) return;
            Current = s;
            Log.Info("Settings reloaded from disk");
            ExternallyChanged?.Invoke(s);
        }
        catch (Exception ex) { Log.Warn($"Ignoring invalid hand-edited settings: {ex.Message}"); }
    }

    public static Settings Clone(Settings s) =>
        JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(s, JsonOpts), JsonOpts)!;

    public static string Export(Settings s) => JsonSerializer.Serialize(s, JsonOpts);
    public static Settings Import(string json) => JsonSerializer.Deserialize<Settings>(json, JsonOpts) ?? new Settings();

    public void Dispose() => _watcher?.Dispose();
}
