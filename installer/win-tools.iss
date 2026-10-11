#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

[Setup]
AppId={{EA3B8B51-EC83-4F1A-8994-70F49AF6604A}
AppName=WinTools
AppVersion={#AppVersion}
AppPublisher=Alzgaymer
DefaultDirName={localappdata}\Programs\WinTools
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=WinTools-Setup-{#AppVersion}-x64
SetupIconFile=..\src\WinTools.App\Assets\WinTools.ico
UninstallDisplayIcon={app}\WinTools.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
RestartApplications=no

[Tasks]
Name: "startup"; Description: "Start with Windows"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\WinTools"; Filename: "{app}\WinTools.exe"

[Registry]
; Start with Windows (installer checkbox). The app's tray menu toggles the same value.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "WinTools"; ValueData: """{app}\WinTools.exe"""; Tasks: startup
; Always remove it on uninstall, even if it was enabled later from the tray menu.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "WinTools"; Flags: uninsdeletevalue
; Remove the Rust version's startup entry.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "BrightnessCtrl"; Flags: deletevalue

[Run]
Filename: "{app}\WinTools.exe"; Description: "Launch WinTools"; Flags: postinstall nowait skipifsilent
