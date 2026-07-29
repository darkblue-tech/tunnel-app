; Inno Setup Script for DarkTunnel Client Online Web Installer
; Usage: iscc /DAppVersion=1.0.1 /DArch=win-x64 /DDownloadUrl=https://github.com/darkblue-tech/tunnel-app/releases/download/v1.0.1/DarkTunnel-Client-v1.0.1-win-x64.zip web-setup.iss

#ifndef AppVersion
#define AppVersion "1.0.1"
#endif

#ifndef Arch
#define Arch "win-x64"
#endif

#ifndef DownloadUrl
#define DownloadUrl "https://github.com/darkblue-tech/tunnel-app/releases/download/v1.0.1/DarkTunnel-Client-v1.0.1-win-x64.zip"
#endif

#define AppName "DarkTunnel Client"
#define AppPublisher "darkblue.tech"
#define AppURL "https://tunnel.darkblue.tech"
#define AppExeName "DarkTunnel Client.exe"

[Setup]
AppId={{D3F90126-7B89-4E60-8F12-8902A3C41234}}
AppName={#AppName} (Web Setup)
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={autopf}\DarkTunnel Client
DefaultGroupName=DarkTunnel Client
DisableProgramGroupPage=yes
OutputBaseFilename=DarkTunnel-Client-WebSetup-v{#AppVersion}-{#Arch}
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

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Code]
var
  DownloadPage: TWizardPage;

procedure InitializeWizard;
begin
  // Create status page for web download
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
  ZipFile: string;
  PsScript: string;
begin
  Result := True;
  if CurPageID = wpReady then
  begin
    ZipFile := ExpandConstant('{tmp}\payload.zip');
    
    // Download payload via PowerShell Net.WebClient / Invoke-WebRequest
    PsScript := Format(
      'Set-ExecutionPolicy Bypass -Scope Process -Force; ' +
      '[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; ' +
      '(New-Object System.Net.WebClient).DownloadFile(''%s'', ''%s'')',
      ['{#DownloadUrl}', ZipFile]
    );

    Exec('powershell.exe', '-NoProfile -NonInteractive -Command "' + PsScript + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    
    if ResultCode <> 0 then
    begin
      MsgBox('Failed to download release payload from ' + '{#DownloadUrl}', mbError, MB_OK);
      Result := False;
      Exit;
    end;

    // Unpack ZIP into {app}
    PsScript := Format(
      'Expand-Archive -Path ''%s'' -DestinationPath ''%s'' -Force',
      [ZipFile, ExpandConstant('{app}')]
    );
    Exec('powershell.exe', '-NoProfile -NonInteractive -Command "' + PsScript + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    
    if ResultCode <> 0 then
    begin
      MsgBox('Failed to extract application payload.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;
end;

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
