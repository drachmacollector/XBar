# XBar emergency restore - GPL-3.0-or-later
# Puts the taskbar back to normal without needing XBar itself.
#   powershell -ExecutionPolicy Bypass -File restore.ps1            # reset shape + work area
#   powershell -ExecutionPolicy Bypass -File restore.ps1 -Explorer  # also restart Explorer (always works)
param([switch]$Explorer)

Get-Process XBar -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

Add-Type @'
using System; using System.Runtime.InteropServices;
public static class XR {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [StructLayout(LayoutKind.Sequential)] public struct ABD { public int cb; public IntPtr h; public uint m; public uint e; public RECT rc; public IntPtr lp; }
  [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string w);
  [DllImport("user32.dll")] public static extern int SetWindowRgn(IntPtr h, IntPtr r, bool d);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint f);
  [StructLayout(LayoutKind.Sequential)] public struct MI { public int cb; public RECT mon; public RECT work; public uint f; }
  [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr m, ref MI i);
  [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint a, uint b, ref RECT r, uint f);
  [DllImport("shell32.dll")] public static extern IntPtr SHAppBarMessage(uint m, ref ABD d);
}
'@
[void][XR]::SetThreadDpiAwarenessContext([IntPtr]-4)
$d = New-Object XR+ABD; $d.cb = [Runtime.InteropServices.Marshal]::SizeOf($d)
$autoHide = ([long][XR]::SHAppBarMessage(4, [ref]$d) -band 1) -ne 0

$bars = @()
$h = [XR]::FindWindowEx([IntPtr]::Zero, [IntPtr]::Zero, "Shell_TrayWnd", $null); if ($h -ne [IntPtr]::Zero) { $bars += $h }
$p = [IntPtr]::Zero
while ($true) { $p = [XR]::FindWindowEx([IntPtr]::Zero, $p, "Shell_SecondaryTrayWnd", $null); if ($p -eq [IntPtr]::Zero) { break }; $bars += $p }

foreach ($b in $bars) {
  [void][XR]::SetWindowRgn($b, [IntPtr]::Zero, $true)
  $r = New-Object XR+RECT; [void][XR]::GetWindowRect($b, [ref]$r)
  $mi = New-Object XR+MI; $mi.cb = [Runtime.InteropServices.Marshal]::SizeOf($mi)
  [void][XR]::GetMonitorInfo([XR]::MonitorFromWindow($b, 2), [ref]$mi)
  $w = $mi.mon
  if (-not $autoHide) { $w.B = $mi.mon.B - ($r.B - $r.T) }
  [void][XR]::SystemParametersInfo(0x2F, 0, [ref]$w, 2)
  Write-Host "Restored taskbar $b; work area bottom = $($w.B)"
}

$j = Join-Path $env:LOCALAPPDATA "XBar\state.json"
if (Test-Path $j) { (Get-Content $j -Raw) -replace '"Dirty":\s*true', '"Dirty": false' | Set-Content $j -Encoding utf8 }

if ($Explorer) { Stop-Process -Name explorer -Force; Start-Sleep 1; if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe } }
Write-Host "Done."
