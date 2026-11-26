; XBar installer (Inno Setup 6) - GPL-3.0-or-later
; Build with tools\make-installer.ps1, which stages the files and passes /DAppVersion=... /DSourceDir=...
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\stage"
#endif

[Setup]
AppId={{B7E1D2A4-5C3F-4E8A-9D61-2F0A8C47E9B3}
AppName=XBar
AppVersion={#AppVersion}
AppVerName=XBar {#AppVersion}
AppPublisher=XBar
AppPublisherURL=https://github.com/drachmacollector/My-Taskbar
AppSupportURL=https://github.com/drachmacollector/My-Taskbar/issues
AppUpdatesURL=https://github.com/drachmacollector/My-Taskbar/releases
DefaultDirName={autopf}\XBar
DefaultGroupName=XBar
DisableProgramGroupPage=yes
; Per-user install: no admin rights needed, matching how XBar runs.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 11 only (build 22000+).
MinVersion=10.0.22000
LicenseFile={#SourceDir}\LICENSE
SetupIconFile=..\src\XBar.App\Assets\xbar.ico
UninstallDisplayIcon={app}\XBar.exe
OutputDir=..\artifacts\installer
OutputBaseFilename=XBar-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start XBar when I sign in"; GroupDescription: "Startup:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\XBar\XBar"; Filename: "{app}\XBar.exe"; Comment: "XBar control panel"
Name: "{autoprograms}\XBar\XBar - Restore taskbar"; Filename: "{app}\XBar.exe"; Parameters: "--restore"; IconFilename: "{app}\icons\restore.ico"; Comment: "Put the taskbar back to normal right now"
Name: "{autoprograms}\XBar\XBar - Safe mode"; Filename: "{app}\XBar.exe"; Parameters: "--safe"; IconFilename: "{app}\icons\safe.ico"; Comment: "Start XBar without applying any changes"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "XBar"; ValueData: """{app}\XBar.exe"" --startup"; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\XBar.exe"; Description: "Launch XBar"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Put the taskbar back first, then make sure no XBar process (app or watchdog) is left holding files.
Filename: "{app}\XBar.exe"; Parameters: "--restore"; Flags: runhidden waituntilterminated; RunOnceId: "RestoreTaskbar"
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im XBar.exe"; Flags: runhidden waituntilterminated; RunOnceId: "KillXBar"

[UninstallDelete]
Type: filesandordirs; Name: "{autoprograms}\XBar"

[Code]

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    if MsgBox('Also delete your XBar settings and logs?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{localappdata}\XBar'), True, True, True);
end;
