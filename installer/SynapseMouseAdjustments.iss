; Optional installer script for Inno Setup 6 (https://jrsoftware.org/isinfo.php).
; 1. Run .\build.ps1 first (creates publish\win-x64\SynapseMouseAdjustments.exe).
; 2. Compile this script with the Inno Setup Compiler.
; Installs per-user (no administrator rights), adds a Start-menu shortcut, and on uninstall restores the
; user's Windows mouse settings and removes the "Start with Windows" entry.

#define AppName "Synapse Mouse Adjustments"
; The release workflow passes the version with /DAppVersion=x.y.z
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppExe "SynapseMouseAdjustments.exe"

[Setup]
AppId={{7B6E0F7A-3C2D-4A7E-9B61-5E8D2C4F1A90}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Synapse Mouse Adjustments contributors
DefaultDirName={localappdata}\Programs\SynapseMouseAdjustments
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\publish
OutputBaseFilename=SynapseMouseAdjustments-{#AppVersion}-Setup-x64
SetupIconFile=..\src\SynapseMouse.App\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes

[Files]
Source: "..\publish\win-x64\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Restore Windows mouse settings and remove the startup entry before files are deleted.
Filename: "{app}\{#AppExe}"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "SynapseCleanup"
