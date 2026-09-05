; Inno Setup Script for DarkTunnel Client Standalone Offline Installer
; Usage: iscc /DAppVersion=1.0.3 /DArch=win-x64 /DSourceDir=..\out\win-x64 setup.iss

#ifndef AppVersion
#define AppVersion "1.0.3"
#endif

#ifndef Arch
#define Arch "win-x64"
#endif

#ifndef SourceDir
#define SourceDir "..\out\win-x64"
#endif

#define AppName "DarkTunnel Client"
#define AppPublisher "darkblue.tech"
#define AppURL "https://tunnel.darkblue.tech"
#define AppExeName "DarkTunnel Client.exe"

[Setup]
AppId={{D3F90126-7B89-4E60-8F12-8902A3C41234}}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={autopf}\DarkTunnel Client
DefaultGroupName=DarkTunnel Client
DisableProgramGroupPage=yes
OutputBaseFilename=DarkTunnel-Client-Setup-v{#AppVersion}-{#Arch}
OutputDir=..\out\installers
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible arm64
ArchitecturesInstallIn64BitMode=x64compatible arm64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\darktunnel"; ValueType: string; ValueName: ""; ValueData: "URL:DarkTunnel Protocol"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\darktunnel"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\darktunnel\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName},0"
Root: HKA; Subkey: "Software\Classes\darktunnel\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runasoriginaluser
