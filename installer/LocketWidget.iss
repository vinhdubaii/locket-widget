; Inno Setup script: one small wizard, installs per user (no admin),
; starts the widget with Windows and removes itself cleanly.
; Build:  ISCC /DMyAppVersion=0.1.0 installer\LocketWidget.iss

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

; folder produced by: dotnet publish ... -o publish
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
AppId={{843E786B-CF60-4000-8833-386D1D18505F}
AppName=Locket Widget
AppVersion={#MyAppVersion}
AppPublisher=vinhdubaii
DefaultDirName={autopf}\Locket Widget
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=LocketWidget-Setup-{#MyAppVersion}
SetupIconFile=..\src\img\icon.ico
UninstallDisplayIcon={app}\LocketWidget.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Locket Widget"; Filename: "{app}\LocketWidget.exe"

[Registry]
; run when the user signs in to Windows
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "LocketWidget"; ValueData: """{app}\LocketWidget.exe"""; Flags: uninsdeletevalue

[Run]
Filename: "{app}\LocketWidget.exe"; Description: "Launch Locket Widget"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM LocketWidget.exe"; Flags: runhidden; RunOnceId: "StopLocketWidget"
