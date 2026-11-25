// XBar - GPL-3.0-or-later
using System.Diagnostics;
using XBar.Core;
using XBar.Core.Engine;
using XBar.Core.Native;

namespace XBar.App;

/// <summary>
/// Separate process (XBar.exe --watchdog pid ticks) that restores the taskbar if the
/// main process dies (even via taskkill /f) or hangs.
/// </summary>
public static class Watchdog
{
    public static int Run(string[] args)
    {
        int i = Array.FindIndex(args, x => x.Equals("--watchdog", StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i + 1 >= args.Length || !uint.TryParse(args[i + 1], out uint pid)) return 2;

        IntPtr h = Win32.OpenProcess(Win32.SYNCHRONIZE | Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) { Log.Warn($"Watchdog: cannot open {pid}"); return 3; }
        Log.Info($"Watchdog watching {pid}");

        int hungStrikes = 0;
        bool restoredForHang = false;
        try
        {
            while (true)
            {
                uint r = Win32.WaitForSingleObject(h, 5000);
                if (r == 0) break; // process exited

                // Hang detection: the host window must answer within 5 s, three times in a row.
                var host = Win32.FindWindow(null, TapProtocol.AppWindowTitle);
                if (host != IntPtr.Zero)
                {
                    var ok = Win32.SendMessageTimeout(host, Win32.WM_NULL, IntPtr.Zero, IntPtr.Zero, Win32.SMTO_ABORTIFHUNG, 5000, out _);
                    hungStrikes = ok == IntPtr.Zero ? hungStrikes + 1 : 0;
                    if (hungStrikes >= 3 && !restoredForHang)
                    {
                        RestoreService.RestoreAll("watchdog: XBar is not responding");
                        restoredForHang = true;
                    }
                    if (hungStrikes == 0) restoredForHang = false;
                }
            }
        }
        finally { Win32.CloseHandle(h); }

        var j = SafetyJournal.Read();
        if (j is { Dirty: true } && j.Pid == (int)pid)
        {
            Log.Warn("Watchdog: XBar exited with changes applied; restoring");
            RestoreService.RestoreAll("watchdog: owner died");
        }
        else Log.Info("Watchdog: owner exited cleanly");
        return 0;
    }

    public static Process? Launch()
    {
        try
        {
            var exe = Environment.ProcessPath!;
            var psi = new ProcessStartInfo(exe, $"--watchdog {Environment.ProcessId}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            return Process.Start(psi);
        }
        catch (Exception ex)
        {
            Log.Error("Could not start watchdog", ex);
            return null;
        }
    }
}
