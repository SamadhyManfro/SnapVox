; SnapVox installer (Inno Setup 6). Built by build.ps1, no need to open it by hand.
; Installs for the current user only (no administrator rights) to %LOCALAPPDATA%\Programs\SnapVox.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

[Setup]
AppId={{5A1F2C7E-9B3D-4E61-A0B8-53A0C9D7F1E2}
AppName=SnapVox
AppVersion={#AppVersion}
AppVerName=SnapVox {#AppVersion}
AppPublisher=Samadhy
AppPublisherURL=https://github.com/SamadhyManfro/SnapVox
AppSupportURL=https://github.com/SamadhyManfro/SnapVox/issues
AppUpdatesURL=https://github.com/SamadhyManfro/SnapVox/releases
DefaultDirName={localappdata}\Programs\SnapVox
DefaultGroupName=SnapVox
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\artifacts
OutputBaseFilename=SnapVox-Setup-{#AppVersion}
SetupIconFile=..\assets\SnapVox.ico
UninstallDisplayIcon={app}\SnapVox.exe
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
; Detects a running SnapVox before updating (the single-instance check uses this mutex).
AppMutex=Local\SnapVox.SingleInstance
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[CustomMessages]
english.StartWithWindows=Start SnapVox with Windows
brazilianportuguese.StartWithWindows=Iniciar o SnapVox com o Windows
english.ShowDemo=See how SnapVox works
brazilianportuguese.ShowDemo=Ver como o SnapVox funciona
english.Shortcut=Shortcut: Win + Ctrl + R
brazilianportuguese.Shortcut=Atalho: Win + Ctrl + R

[Tasks]
Name: "startup"; Description: "{cm:StartWithWindows}"; Flags: checkedonce
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\docs\demo.html"; DestDir: "{app}\demo"; DestName: "SnapVox-demo.html"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\SnapVox"; Filename: "{app}\SnapVox.exe"; Comment: "{cm:Shortcut}"
Name: "{autodesktop}\SnapVox"; Filename: "{app}\SnapVox.exe"; Tasks: desktopicon

[Registry]
; Start with Windows (the same key the app toggles in Settings).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SnapVox"; ValueData: """{app}\SnapVox.exe"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\demo\SnapVox-demo.html"; Description: "{cm:ShowDemo}"; Flags: postinstall shellexec skipifsilent unchecked
Filename: "{app}\SnapVox.exe"; Description: "{cm:LaunchProgram,SnapVox}"; Flags: postinstall nowait skipifsilent
; Automatic updates run the installer in silent mode: reopen SnapVox when it finishes.
Filename: "{app}\SnapVox.exe"; Flags: nowait; Check: WizardSilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/c taskkill /im SnapVox.exe /f"; Flags: runhidden; RunOnceId: "KillSnapVox"

[UninstallDelete]
; Downloaded voices and the log live in %LOCALAPPDATA%\SnapVox; settings in %APPDATA%\SnapVox.
Type: filesandordirs; Name: "{localappdata}\SnapVox"
