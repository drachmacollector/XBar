# Rebuild (Release, C# only) and restart XBar from .\publish\XBar so a change can be seen on the real taskbar.
#   .\tools\rerun.ps1            stop XBar, build, relaunch
#   .\tools\rerun.ps1 -WithTap   also rebuild the C++ TAP module (needs VS Build Tools)
param([switch]$WithTap)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

# Stop the app only (not the watchdog): the watchdog then restores the taskbar before we relaunch.
Get-CimInstance Win32_Process -Filter "Name='XBar.exe'" |
    Where-Object { $_.CommandLine -notmatch '--watchdog' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
Start-Sleep -Seconds 4

if ($WithTap) { & "$root\build.ps1" -Release } else { & "$root\build.ps1" -Release -SkipTap }
if ($LASTEXITCODE) { throw "build failed" }

Start-Process "$root\publish\XBar\XBar.exe"
Start-Sleep -Seconds 7
Write-Host "XBar relaunched."
