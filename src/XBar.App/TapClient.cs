// XBar - GPL-3.0-or-later
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using XBar.Core;
using XBar.Core.Engine;
using XBar.Core.Native;

namespace XBar.App;

/// <summary>
/// Talks to XBar.Tap.dll, which lives inside explorer.exe and repaints the XAML
/// taskbar (brushes, rounded anti-aliased pills, border, shadow, opacity).
/// Transport: WM_COPYDATA in both directions; see TapProtocol.
/// </summary>
public sealed class TapClient : IAppearanceSink, IDisposable
{
    const string TranslucentTbMutex = "344635E9-9AE4-4E60-B128-D53E25AB70A7";

    readonly IntPtr _appHwnd;
    readonly Dictionary<IntPtr, string> _lastSent = new();
    readonly Dictionary<IntPtr, (TaskbarGeometry Geo, DateTime At)> _geometry = new();
    IntPtr _tapHwnd;
    int _tapVersion;
    string _status = "Not loaded";

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int InjectFn(IntPtr taskbar, [MarshalAs(UnmanagedType.LPWStr)] string endpoint, uint timeoutMs);

    public TapClient(IntPtr appHwnd)
    {
        _appHwnd = appHwnd;
        GeometryProvider.TapOverride = h =>
        {
            lock (_geometry)
                return _geometry.TryGetValue(h, out var g) && DateTime.UtcNow - g.At < TimeSpan.FromSeconds(30) ? g.Geo : null;
        };
    }

    public bool IsAvailable => _tapHwnd != IntPtr.Zero && Win32.IsWindow(_tapHwnd) && _tapVersion == TapProtocol.Version;
    public bool IsConnecting { get; private set; }
    public string Status => _status;
    public event Action? Changed;

    public static string BundledDllPath => Path.Combine(AppContext.BaseDirectory, "XBar.Tap.dll");

    public async Task<bool> ConnectAsync()
    {
        if (IsConnecting) return false;
        IsConnecting = true;
        try
        {
            return await Task.Run(Connect);
        }
        finally
        {
            IsConnecting = false;
            Application.Current?.Dispatcher.BeginInvoke(() => Changed?.Invoke());
        }
    }

    bool Connect()
    {
        if (Mutex.TryOpenExisting(TranslucentTbMutex, out var m))
        {
            m.Dispose();
            SetStatus("TranslucentTB is running. Quit it (and disable its startup) to use XBar's styling.");
            return false;
        }
        if (!File.Exists(BundledDllPath))
        {
            SetStatus("Appearance module (XBar.Tap.dll) is missing from the install folder.");
            return false;
        }
        var taskbar = Win32.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero) { SetStatus("Explorer taskbar not found"); return false; }

        // Each build gets a unique file name: Explorer pins a loaded TAP forever, so a rebuilt
        // DLL must load under a new name, and the old one is asked to go dormant first.
        var (path, tag) = StageDll();

        // Reuse our own TAP if this Explorer already has it; ask any other (older) build to go dormant.
        for (IntPtr prev = IntPtr.Zero; ;)
        {
            var w = Win32.FindWindowEx(IntPtr.Zero, prev, TapProtocol.TapWindowClass, null);
            if (w == IntPtr.Zero) break;
            prev = w;
            if (Win32.WindowText(w) == tag)
            {
                if (Hello(w) == TapProtocol.Version)
                {
                    _tapHwnd = w;
                    _lastSent.Clear();
                    SetStatus("Connected");
                    SafetyJournal.Update(j => { j.Dirty = true; j.TapActive = true; });
                    return true;
                }
            }
            else
            {
                Log.Info($"Asking an older TAP instance ({Win32.WindowText(w)}) to go dormant");
                Send(w, TapProtocol.CmdShutdown, "");
            }
        }

        IntPtr mod = Win32.LoadLibrary(path);
        if (mod == IntPtr.Zero) { SetStatus($"Could not load {Path.GetFileName(path)} (error {Marshal.GetLastWin32Error()})"); return false; }
        try
        {
            var fp = Win32.GetProcAddress(mod, "XBarInject");
            if (fp == IntPtr.Zero) { SetStatus("XBarInject export missing"); return false; }
            var inject = Marshal.GetDelegateForFunctionPointer<InjectFn>(fp);
            SetStatus("Loading into Explorer…");
            int hr = inject(taskbar, tag, 15000);
            if (hr < 0)
            {
                SetStatus($"Injection failed (0x{hr:X8}). See log.");
                Log.Error($"XBarInject returned 0x{hr:X8}");
                return false;
            }
        }
        finally
        {
            // Our own copy is not needed; Explorer holds its own reference.
            Win32.FreeLibrary(mod);
        }

        for (int i = 0; i < 50; i++)
        {
            IntPtr h = IntPtr.Zero;
            // Find the window whose title is our tag (an older dormant one may also exist).
            for (IntPtr prev = IntPtr.Zero; ;)
            {
                var c = Win32.FindWindowEx(IntPtr.Zero, prev, TapProtocol.TapWindowClass, null);
                if (c == IntPtr.Zero) break;
                if (Win32.WindowText(c) == tag) { h = c; break; }
                prev = c;
            }
            if (h != IntPtr.Zero && Hello(h) == TapProtocol.Version)
            {
                _tapHwnd = h;
                _lastSent.Clear();
                SetStatus("Connected");
                Log.Info("TAP connected");
                SafetyJournal.Update(j => { j.Dirty = true; j.TapActive = true; });
                return true;
            }
            Thread.Sleep(100);
        }
        SetStatus("Explorer did not respond to the appearance module.");
        return false;
    }

    (string Path, string Tag) StageDll()
    {
        var bytes = File.ReadAllBytes(BundledDllPath);
        var hash = Convert.ToHexString(SHA256.HashData(bytes))[..12];
        Directory.CreateDirectory(Paths.TapDir);
        var target = Path.Combine(Paths.TapDir, $"XBar.Tap.{hash}.dll");
        if (!File.Exists(target)) File.WriteAllBytes(target, bytes);
        // Clean up old copies that are not loaded anywhere (locked ones just fail).
        foreach (var f in Directory.GetFiles(Paths.TapDir, "XBar.Tap.*.dll"))
            if (!f.Equals(target, StringComparison.OrdinalIgnoreCase)) try { File.Delete(f); } catch { }
        return (target, "XBarTap_" + hash);
    }

    int Hello(IntPtr h)
    {
        _tapVersion = 0;
        var reply = Send(h, TapProtocol.CmdHello, $"pid={Environment.ProcessId};version={TapProtocol.Version};app={_appHwnd.ToInt64()};");
        // The TAP answers synchronously through the return value: its protocol version.
        _tapVersion = (int)reply;
        return _tapVersion;
    }

    long Send(IntPtr hwnd, int cmd, string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload + "\0");
        var mem = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, mem, bytes.Length);
            var cds = new COPYDATASTRUCT { dwData = new IntPtr(cmd), cbData = bytes.Length, lpData = mem };
            var ok = Win32.SendMessageTimeout(hwnd, Win32.WM_COPYDATA, _appHwnd, ref cds, Win32.SMTO_ABORTIFHUNG, 3000, out var result);
            return ok == IntPtr.Zero ? -1 : result.ToInt64();
        }
        finally { Marshal.FreeHGlobal(mem); }
    }

    public void Apply(AppearanceSpec spec)
    {
        if (!IsAvailable) return;
        var payload = spec.Serialize();
        if (_lastSent.TryGetValue(spec.Taskbar, out var last) && last == payload) return;
        _lastSent[spec.Taskbar] = payload;
        if (Send(_tapHwnd, TapProtocol.CmdAppearance, payload) < 0)
            Log.Warn("TAP did not accept appearance");
        else if (!_journaled)
        {
            SafetyJournal.Update(j => { j.Dirty = true; j.TapActive = true; });
            _journaled = true;
        }
    }

    bool _journaled;

    public void SetOpacity(IntPtr taskbar, double opacity, int ms)
    {
        if (!IsAvailable) return;
        Send(_tapHwnd, TapProtocol.CmdOpacity,
            string.Create(CultureInfo.InvariantCulture, $"hwnd={taskbar.ToInt64()};opacity={opacity};ms={ms};"));
    }

    public void DumpTree()
    {
        if (IsAvailable) Send(_tapHwnd, TapProtocol.CmdDumpTree, "");
    }

    public void RestoreAll()
    {
        _lastSent.Clear();
        var h = _tapHwnd != IntPtr.Zero && Win32.IsWindow(_tapHwnd) ? _tapHwnd : Win32.FindWindow(TapProtocol.TapWindowClass, null);
        if (h != IntPtr.Zero) Send(h, TapProtocol.CmdRestore, "");
        SafetyJournal.Update(j => j.TapActive = false);
        _journaled = false;
    }

    public void OnTaskbarRecreated()
    {
        // Explorer restarted: the TAP died with it.
        _tapHwnd = IntPtr.Zero;
        _lastSent.Clear();
        lock (_geometry) _geometry.Clear();
        SetStatus("Explorer restarted; reconnecting…");
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        t.Tick += async (_, _) => { t.Stop(); if (await ConnectAsync()) App.Host.Engine?.Rebuild("tap reconnected"); };
        t.Start();
    }

    /// <summary>Messages from the TAP arrive at the app's message window.</summary>
    public bool HandleCopyData(IntPtr sender, IntPtr lParam)
    {
        var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
        int cmd = cds.dwData.ToInt32();
        if (cmd < 100 || cmd > 199) return false;
        string text = cds.cbData > 0 ? Marshal.PtrToStringUTF8(cds.lpData, cds.cbData).TrimEnd('\0') : "";
        var kv = Parse(text);

        switch (cmd)
        {
            case TapProtocol.EvtLog:
                Log.Info("[tap] " + text);
                break;
            case TapProtocol.EvtGeometry:
                if (kv.TryGetValue("hwnd", out var hs) && long.TryParse(hs, out var hv)
                    && Win32.GetWindowRect(new IntPtr(hv), out var tbRect))
                {
                    var apps = ParseRect(kv.GetValueOrDefault("apps"));
                    var tray = ParseRect(kv.GetValueOrDefault("tray"));
                    lock (_geometry) _geometry[new IntPtr(hv)] = (new TaskbarGeometry(tbRect, apps, tray), DateTime.UtcNow);
                }
                break;
        }
        return true;
    }

    static Dictionary<string, string> Parse(string s)
    {
        var d = new Dictionary<string, string>();
        foreach (var part in s.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int i = part.IndexOf('=');
            if (i > 0) d[part[..i]] = part[(i + 1)..];
        }
        return d;
    }

    static RECT ParseRect(string? s)
    {
        if (string.IsNullOrEmpty(s)) return default;
        var p = s.Split(',');
        if (p.Length != 4) return default;
        return new RECT(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]));
    }

    void SetStatus(string s)
    {
        _status = s;
        Log.Info($"TAP: {s}");
        Application.Current?.Dispatcher.BeginInvoke(() => Changed?.Invoke());
    }

    public void Dispose()
    {
        RestoreAll();
        if (_tapHwnd != IntPtr.Zero) Send(_tapHwnd, TapProtocol.CmdShutdown, "");
        _tapHwnd = IntPtr.Zero;
    }
}
