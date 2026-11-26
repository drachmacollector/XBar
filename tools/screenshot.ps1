# Capture the screen (DPI-correct) to a PNG so taskbar changes can be reviewed by eye.
#   .\tools\screenshot.ps1 -Out shot.png                 whole screen
#   .\tools\screenshot.ps1 -Out bar.png -BottomStrip 110 just the bottom 110 px (the taskbar)
#   .\tools\screenshot.ps1 -Out panel.png -Panel         open + focus the XBar control panel first
param([Parameter(Mandatory)][string]$Out, [int]$BottomStrip = 0, [switch]$Panel)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public class XbShot {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, int flags, int extra);
}
"@
[XbShot]::SetProcessDPIAware() | Out-Null
if ($Panel) {
    # A second launch asks the running instance to show its control panel.
    Start-Process (Join-Path (Split-Path $PSScriptRoot -Parent) 'publish\XBar\XBar.exe'); Start-Sleep 3
    Get-Process XBar | Where-Object MainWindowHandle -ne 0 | ForEach-Object {
        [XbShot]::ShowWindow($_.MainWindowHandle, 9) | Out-Null
        [XbShot]::keybd_event(0x12, 0, 0, 0); [XbShot]::keybd_event(0x12, 0, 2, 0)   # tap Alt so Windows allows the focus change
        [XbShot]::SetForegroundWindow($_.MainWindowHandle) | Out-Null
    }
    Start-Sleep 1
}
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
[System.Drawing.Graphics]::FromImage($bmp).CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
if ($BottomStrip -gt 0) {
    $bmp = $bmp.Clone((New-Object System.Drawing.Rectangle 0, ($b.Height - $BottomStrip), $b.Width, $BottomStrip), $bmp.PixelFormat)
}
$bmp.Save($Out)
Write-Host "Saved $Out"
