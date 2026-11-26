# Build the XBar installer (self-contained, no .NET install needed on the target PC).
#   .\tools\make-installer.ps1                 full build (C++ module + app), stage, compile installer
#   .\tools\make-installer.ps1 -SkipBuild      reuse the existing artifacts\tap\XBar.Tap.dll
# Output: artifacts\installer\XBar-Setup-<version>.exe. Needs Inno Setup 6 or 7 (https://jrsoftware.org/isinfo.php).
param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$version = ([xml](Get-Content "$root\Directory.Build.props")).Project.PropertyGroup.Version
if (-not $version) { throw "Version not found in Directory.Build.props" }

$iscc = @("$env:ProgramFiles\Inno Setup 7\ISCC.exe","$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 or 7 (ISCC.exe) not found. Install it from https://jrsoftware.org/isdl.php" }

if (-not $SkipBuild) { & "$root\build.ps1" -Release }

$stage = "$root\artifacts\stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
Write-Host "== Publish self-contained ($version)" -ForegroundColor Cyan
& dotnet publish "$root\src\XBar.App\XBar.App.csproj" -c Release -p:Platform=x64 -r win-x64 --self-contained true -o $stage --nologo -v minimal
if ($LASTEXITCODE) { throw "publish failed" }
if (-not (Test-Path "$stage\XBar.Tap.dll")) { throw "XBar.Tap.dll missing from the staged build (build the C++ module first)" }
Get-ChildItem $stage -Filter *.pdb | Remove-Item -Force
Copy-Item "$root\LICENSE", "$root\README.md", "$root\THIRD_PARTY_NOTICES.md" $stage -Force

Write-Host "== Compile installer" -ForegroundColor Cyan
& $iscc "/DAppVersion=$version" "/DSourceDir=$stage" "$root\installer\XBar.iss"
if ($LASTEXITCODE) { throw "Inno Setup failed" }
Write-Host "Installer: $root\artifacts\installer\XBar-Setup-$version.exe" -ForegroundColor Green
