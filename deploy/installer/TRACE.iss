; TRACE installer (Inno Setup 6).  UNTESTED: not yet compiled or run.
; Build the bundle first:  deploy\build-release.ps1   ->  dist\TRACE.bundle
;
; Installs the Autodesk application bundle for all users. Civil 3D loads it at startup;
; nobody runs NETLOAD. User configuration lives in %APPDATA% and is NEVER touched by
; install, upgrade or uninstall.

#define AppName "TRACE"
#define AppVersion "0.1.0"

[Setup]
AppId={{B7C4A1E2-5D3F-4A89-9E6B-2C1F0D8A7B35}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=TRACE
DefaultDirName={commonpf64}\Autodesk\ApplicationPlugins\TRACE.bundle
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\..\dist
OutputBaseFilename=TRACE-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
UninstallDisplayName={#AppName}
; Civil 3D holds the plugin assembly open for the life of the process: ask the user
; to close it (Restart Manager) instead of failing on a locked file.
CloseApplications=yes
CloseApplicationsFilter=acad.exe

[Files]
Source: "..\..\dist\TRACE.bundle\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
