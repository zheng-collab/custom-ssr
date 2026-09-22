; SecureGateway Windows installer (Inno Setup 6 — https://jrsoftware.org/isinfo.php)
;
; Built automatically by:  .\scripts\build.ps1 -Publish -Installer
; which passes MyAppVersion, SourceDir (the publish folder) and OutputDir.
; Manual build from the repo root after a publish:
;   ISCC.exe /DMyAppVersion=1.0.0 /DSourceDir=build\publish /DOutputDir=build installer\SecureGateway.iss

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\build\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\build"
#endif

#define MyAppName      "SecureGateway"
#define MyAppPublisher "Fourth Zodiac"
#define MyAppURL       "https://fourthzodiac.com"
#define MyAppExeName   "SecureGateway.exe"

[Setup]
; Never change AppId: it is how Windows recognises upgrades of the same product.
AppId={{6F1E1C3A-7B2D-4C1E-9A5B-3D2F8E4C7A10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
OutputDir={#OutputDir}
OutputBaseFilename=SecureGateway-Setup-{#MyAppVersion}
SetupIconFile=..\src\SecureGateway\Assets\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; Install for all users when run as admin, otherwise offer a per-user install
; (so staff without admin rights can still install it under their profile).
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
; Close a running SecureGateway before upgrading instead of failing on locked files.
CloseApplications=yes
RestartApplications=no
DisableProgramGroupPage=yes
ShowLanguageDialog=auto

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Everything the publish step produced: SecureGateway.exe plus the v2ray-core folder.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Make sure the engine is not left running when the app is removed.
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM v2ray.exe"; Flags: runhidden; RunOnceId: "KillV2Ray"

[UninstallDelete]
; Per-user data (settings, saved session) lives in %APPDATA%\SecureGateway; leave it
; so a reinstall keeps the user's servers. Only remove what we created under {app}.
Type: filesandordirs; Name: "{app}\v2ray-core"

[Code]
// If SecureGateway is running, CloseApplications handles it; v2ray.exe is a child
// process the app kills on exit, but be safe during upgrades.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM v2ray.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
