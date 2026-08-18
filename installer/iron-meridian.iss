; Iron Meridian — Windows installer (Inno Setup 6)
;
; Normally built through scripts\build-installer.ps1, which fills in the
; version, the player folder and the executable name. It also compiles
; standalone from this folder once a player exists in Builds\Windows:
;
;     "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" iron-meridian.iss
;
; See docs/34-INSTALLER.md.

#define AppName        "Iron Meridian"
#define AppPublisher   "Ivan Sostarko"
#define AppUrl         "https://github.com/ivansostarko/iron-meridian"

#ifndef AppVersion
  #define AppVersion   "1.0"
#endif
#ifndef VersionInfo
  #define VersionInfo  "1.0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir    "..\Builds\Windows"
#endif
#ifndef ExeName
  #define ExeName      "IronMeridian.exe"
#endif
#ifndef OutDir
  #define OutDir       "..\Builds\Installer"
#endif

#if !FileExists(AddBackslash(SourceDir) + ExeName)
  #error Player build not found. Run scripts\build-windows.ps1 first — see docs/34-INSTALLER.md.
#endif

; The Cesium ion token is a secret (golden rule 1). It is left out of the
; package unless -IncludeToken was passed to scripts\build-installer.ps1.
#ifdef IncludeToken
  #define TokenExclude ""
#else
  #define TokenExclude ",cesium-token.txt"
#endif

[Setup]
; Never change AppId — it is how Windows recognises an existing install and
; how an upgrade replaces it instead of installing a second copy.
AppId={{7F3C1E4A-9B62-4D18-A0E5-2C6D8B41F7A9}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#VersionInfo}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} setup
VersionInfoCopyright=(c) Ivan Sostarko

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayName={#AppName} {#AppVersion}
UninstallDisplayIcon={app}\iron-meridian.ico
AllowNoIcons=yes
DisableProgramGroupPage=yes

OutputDir={#OutDir}
OutputBaseFilename=IronMeridian-{#AppVersion}-Setup
SetupIconFile=assets\iron-meridian.ico
WizardStyle=modern
WizardImageFile=assets\wizard-large-*.bmp
WizardSmallImageFile=assets\wizard-small-*.bmp
WizardImageAlphaFormat=none
InfoAfterFile=notes.txt

Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

; The player is x86-64 only, and Cesium's tile streaming assumes a modern OS.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

; Defaults to a per-user install (no UAC prompt); the wizard's first page
; offers a machine-wide install for anyone who wants one.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline

; The game holds its own files open — let the Restart Manager offer to close a
; running copy rather than failing halfway through an upgrade.
CloseApplications=yes
RestartApplications=no
SetupMutex={#AppName}Setup

#ifdef SignToolName
SignTool={#SignToolName}
SignedUninstaller=yes
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Everything Unity emitted, minus Burst debug symbols (the folder is literally
; named DoNotShip), stray logs, and the ion token unless it was asked for.
Source: "{#SourceDir}\*"; DestDir: "{app}"; \
    Excludes: "*_DoNotShip,*_BackUpThisFolder_ButDontShipItWithYourGame,*.pdb,*.log{#TokenExclude}"; \
    Flags: recursesubdirs createallsubdirs ignoreversion
; Unity's player carries the stock engine icon, so ship ours for the shortcuts.
Source: "assets\iron-meridian.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#ExeName}"; \
    IconFilename: "{app}\iron-meridian.ico"; WorkingDir: "{app}"; \
    Comment: "Real-terrain operational wargame"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#ExeName}"; \
    IconFilename: "{app}\iron-meridian.ico"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#ExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; \
    WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Anything the player writes next to itself is not on the install manifest, so
; the uninstaller has to name it or the folder gets left behind half-empty.
Type: filesandordirs; Name: "{app}\Crashes"
Type: dirifempty; Name: "{app}"

[Messages]
BeveledLabel={#AppName} {#AppVersion}

[Code]

{ Unity players link against the Visual C++ 2015-2022 runtime. It is present on
  almost every current Windows install, but a fresh image can be missing it and
  the failure mode — the player exits silently — is impossible to diagnose from
  inside the game. Warn while the user is still in the wizard. }
function VCRuntimePresent: Boolean;
begin
  Result := FileExists(ExpandConstant('{sys}\msvcp140.dll')) and
            FileExists(ExpandConstant('{sys}\vcruntime140_1.dll'));
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  { SuppressibleMsgBox, not MsgBox: a /SILENT install must never stop on a
    dialog nobody is there to answer. Suppressed, it answers Yes and carries
    on — the check is a courtesy, not a gate. }
  if not VCRuntimePresent then
    Result := SuppressibleMsgBox(
      'The Microsoft Visual C++ 2015-2022 Redistributable (x64) does not appear to be installed.' + #13#10#13#10 +
      'Iron Meridian needs it to start, and will close immediately without it.' + #13#10#13#10 +
      'Install it from https://aka.ms/vs/17/release/vc_redist.x64.exe, then run this setup again.' + #13#10#13#10 +
      'Continue anyway?', mbConfirmation, MB_YESNO, IDYES) = IDYES;
end;

{ Saves, the tuning patch and user maps live in Unity's persistent data folder,
  not in the install folder — so an uninstall leaves them behind on purpose.
  Offer to clear them, defaulting to keeping them. }
function SaveFolder: String;
begin
  Result := ExpandConstant('{localappdata}\..\LocalLow\IvanSostarko\Iron Meridian');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    if DirExists(SaveFolder) then
      { Suppressed (a /SILENT uninstall), this answers No and the saves stay —
        the safe half of the question, and the same as the default button. }
      if SuppressibleMsgBox('Delete saved games, custom maps and unit tuning as well?' + #13#10#13#10 +
                SaveFolder + #13#10#13#10 +
                'Choose No to keep them for a future install.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
        DelTree(SaveFolder, True, True, True);
end;
