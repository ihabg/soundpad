; SoundPad — Inno Setup installer script
; Compatible with Inno Setup 6 and 7.
;
; Per-user install (no admin / UAC prompt required).
; Default install path: %LocalAppData%\Programs\SoundPad
;
; Build this with:  scripts\build-installer.ps1
; Or manually:      ISCC.exe installer\SoundPad.iss

#define AppName      "SoundPad"
#define AppVersion   "1.10.0"
#define AppPublisher "Kebab"
#define AppExeName   "SoundPad.App.exe"

; ── Setup ─────────────────────────────────────────────────────────────────────

[Setup]
; AppId must be unique and never change across versions — it links installs/upgrades.
AppId={{9B8E6D3A-4F72-4C91-B5E8-2A1D7F9C3B6E}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}

; Per-user install: no UAC prompt, installs to %LocalAppData%\Programs\SoundPad
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; {autopf} maps to %LocalAppData%\Programs when PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes

; Installer output
OutputDir=..\artifacts\installer
OutputBaseFilename=SoundPad-Setup-{#AppVersion}

; Visuals
SetupIconFile=..\SoundPad.App\Resources\app.ico
WizardStyle=modern
WizardSizePercent=100

; Compression
Compression=lzma2/ultra64
SolidCompression=yes

; Version info embedded in the setup exe
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup

; Uninstaller
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}

; ── Languages ─────────────────────────────────────────────────────────────────

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

; ── Tasks (optional steps the user can choose during install) ─────────────────

[Tasks]
; Desktop shortcut is opt-in so the user's desktop stays clean by default.
Name: "desktopicon"; \
  Description: "{cm:CreateDesktopIcon}"; \
  GroupDescription: "{cm:AdditionalIcons}"; \
  Flags: unchecked

; ── Files ─────────────────────────────────────────────────────────────────────
;
; Install everything produced by dotnet publish (exe + WPF native DLLs +
; bundled Sounds/ and Resources/ subdirectories).
; The app writes its user data to %AppData%\SoundPad at runtime — the
; installer does not touch that directory.

[Files]
Source: "..\artifacts\publish\*"; \
  DestDir: "{app}"; \
  Flags: ignoreversion recursesubdirs createallsubdirs

; ── Shortcuts ─────────────────────────────────────────────────────────────────

[Icons]
; Start Menu shortcut
Name: "{autoprograms}\{#AppName}"; \
  Filename: "{app}\{#AppExeName}"; \
  IconFilename: "{app}\Resources\app.ico"

; Desktop shortcut (only if the user ticked the task above)
Name: "{autodesktop}\{#AppName}"; \
  Filename: "{app}\{#AppExeName}"; \
  IconFilename: "{app}\Resources\app.ico"; \
  Tasks: desktopicon

; ── Post-install launch ───────────────────────────────────────────────────────

[Run]
Filename: "{app}\{#AppExeName}"; \
  Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; \
  Flags: nowait postinstall skipifsilent

; ── Uninstall cleanup ─────────────────────────────────────────────────────────
;
; Removes the application install directory.
; User data (%AppData%\SoundPad — sounds library, settings) is intentionally
; preserved so an uninstall + reinstall does not wipe the user's sound library.

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
