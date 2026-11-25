// XBar - GPL-3.0-or-later
using System.Runtime.InteropServices;
using XBar.Core;
using XBar.Core.Engine;
using XBar.Core.Native;

namespace XBar.App;

public static class Program
{
    public const string MutexName = "XBar.SingleInstance.6F1C2D7E";
    public const string ShowPanelMessage = "XBar_ShowPanel";
    public const string PanicMessage = "XBar_PanicRestore";

    public static bool SafeMode { get; private set; }
    public static bool LaunchedAtStartup { get; private set; }
    public static bool RecoveredFromCrash { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        var a = args.Select(x => x.ToLowerInvariant()).ToArray();

        if (a.Contains("--watchdog"))
        {
            Log.Init("watchdog");
            return Watchdog.Run(args);
        }
        if (a.Contains("--restore"))
        {
            Log.Init("restore");
            SignalRunningInstance(PanicMessage);
            RestoreService.RestoreAll("command line --restore");
            return 0;
        }
        if (a.Contains("--diag"))
        {
            Log.Init("diag");
            var text = DiagnosticsReport.Describe(Displays.GetMonitors(), Displays.GetTaskbars());
            var file = Path.Combine(Paths.LogDir, "diag.txt");
            File.WriteAllText(file, text);
            if (AttachConsole(-1)) Console.WriteLine(Environment.NewLine + text);
            return 0;
        }

        Log.Init("app");
        Log.Info($"XBar {typeof(Program).Assembly.GetName().Version} starting, args: {string.Join(' ', args)}");

        using var mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            Log.Info("Already running; asking it to show the control panel");
            SignalRunningInstance(ShowPanelMessage);
            return 0;
        }

        InstallCrashHandlers();

        // Recovery: a previous run died with changes applied.
        var journal = SafetyJournal.Read();
        if (SafetyJournal.NeedsRecovery(journal))
        {
            Log.Warn("Previous run left changes applied; restoring before anything else");
            RestoreService.RestoreAll("recovery on launch");
            RecoveredFromCrash = true;
        }

        SafeMode = a.Contains("--safe") || (GetAsyncKeyStateShift());
        LaunchedAtStartup = a.Contains("--startup");
        if (LaunchedAtStartup) WaitForTaskbar();

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    static bool GetAsyncKeyStateShift() => (Win32.GetAsyncKeyState(0x10) & 0x8000) != 0;

    static void WaitForTaskbar()
    {
        var until = DateTime.UtcNow.AddSeconds(90);
        while (Win32.FindWindow("Shell_TrayWnd", null) == IntPtr.Zero && DateTime.UtcNow < until)
            Thread.Sleep(500);
        Thread.Sleep(3000); // let Explorer finish laying out
    }

    static void SignalRunningInstance(string message)
    {
        var hwnd = Win32.FindWindow(null, TapProtocol.AppWindowTitle);
        if (hwnd != IntPtr.Zero)
            Win32.SendMessageTimeout(hwnd, (int)Win32.RegisterWindowMessage(message), IntPtr.Zero, IntPtr.Zero, Win32.SMTO_ABORTIFHUNG, 3000, out _);
    }

    static void InstallCrashHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("FATAL unhandled exception", e.ExceptionObject as Exception);
            RestoreService.RestoreAll("fatal crash");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            var j = SafetyJournal.Read();
            if (j is { Dirty: true } && j.Pid == Environment.ProcessId)
                RestoreService.RestoreAll("process exit with changes applied");
        };
    }

    [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
}
