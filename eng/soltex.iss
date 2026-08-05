; Soltex per-user installer.
;
; The application manifest requests asInvoker, and the workspace is designed to
; run unelevated. The installer therefore targets the per-user Programs folder
; and never asks for administrator rights. Build with:
;
;   ISCC.exe /DSourceExe=<path to published Soltex.exe> eng\soltex.iss

#ifndef SourceExe
  #define SourceExe "..\artifacts\publish\win-x64\Soltex.exe"
#endif
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

#define AppName "Soltex"
#define AppPublisher "Soltex"
#define AppUrl "https://github.com/slaveofsolace/soltex"
#define AppExeName "Soltex.exe"

[Setup]
AppId={{8E1F0C6A-5B3D-4A77-9F2E-1C4D6B8A0E52}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoCompany={#AppPublisher}
VersionInfoCopyright=Copyright © 2026. All rights reserved.

; Per-user install: {autopf} resolves to {localappdata}\Programs, no UAC prompt.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes

; The Security page reads Windows Security Center, which needs 10.0.19041+.
MinVersion=10.0.19041
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

LicenseFile=..\LICENSE.txt
SetupIconFile=..\src\Soltex.App\Soltex.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName} {#AppVersion}

OutputDir={#OutputDir}
OutputBaseFilename=Soltex-{#AppVersion}-win-x64-setup
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
CloseApplicationsFilter=*.exe
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; Uninstall deliberately removes only installed program files. Local Soltex
; state (quarantine, audit chain, release-sequence and planning journals) lives
; under the user profile and is left in place so an accidental uninstall cannot
; destroy authenticated history. Remove it manually if that is intended.
