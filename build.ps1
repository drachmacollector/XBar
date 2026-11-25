# XBar build script - GPL-3.0-or-later
#   .\build.ps1                 Debug build of everything
#   .\build.ps1 -Release        Release build + portable folder in .\publish
#   .\build.ps1 -SkipTap        C# only
param([switch]$Release, [switch]$SkipTap, [switch]$Test)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$config = if ($Release) { 'Release' } else { 'Debug' }

if (-not $SkipTap) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    $msbuild = & $vswhere -latest -products * -find 'MSBuild\**\Bin\amd64\MSBuild.exe' | Select-Object -First 1
    if (-not $msbuild) {
        $msbuild = Get-ChildItem "${env:ProgramFiles(x86)}\Microsoft Visual Studio", "$env:ProgramFiles\Microsoft Visual Studio" -Recurse -Filter MSBuild.exe -ErrorAction SilentlyContinue |
            Where-Object FullName -like '*\Bin\amd64\*' | Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $msbuild) { throw "MSBuild with the C++ tools was not found. Install VS Build Tools (Desktop C++ + Windows 11 SDK)." }
    Write-Host "== XBar.Tap ($config)" -ForegroundColor Cyan
    & $msbuild "$root\src\XBar.Tap\XBar.Tap.vcxproj" /p:Configuration=$config /p:Platform=x64 "/p:SolutionDir=$root\" /m /nologo /v:minimal
    if ($LASTEXITCODE) { throw "XBar.Tap build failed" }
}

Write-Host "== XBar ($config)" -ForegroundColor Cyan
& dotnet build "$root\XBar.sln" -c $config -p:Platform=x64 --nologo -v minimal
if ($LASTEXITCODE) { throw "XBar build failed" }

if ($Test) {
    & dotnet test "$root\tests\XBar.Core.Tests" -c $config -p:Platform=x64 --nologo
    if ($LASTEXITCODE) { throw "tests failed" }
}

if ($Release) {
    $out = "$root\publish\XBar"
    & dotnet publish "$root\src\XBar.App\XBar.App.csproj" -c Release -p:Platform=x64 -r win-x64 --self-contained false -o $out --nologo -v minimal
    if ($LASTEXITCODE) { throw "publish failed" }
    Copy-Item "$root\LICENSE", "$root\README.md", "$root\THIRD_PARTY_NOTICES.md" $out -Force
    Write-Host "Portable build: $out" -ForegroundColor Green
}
