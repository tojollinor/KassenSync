#define MyAppName "KassenSync"
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#define MyAppPublisher "KassenSync"
#define MyAppExeName "KassenSync.App.exe"
#define MyServiceExeName "KassenSync.Service.exe"

[Setup]
AppId={{9FC0E580-9938-4E06-A9A4-278689877167}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\KassenSync
DefaultGroupName=KassenSync
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=KassenSync-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\App\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName=KassenSync
VersionInfoDescription=KassenSync Setup
SetupLogging=yes

[Files]
Source: "..\artifacts\publish\app\*"; DestDir: "{app}\App"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\publish\service\*"; DestDir: "{app}\Service"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\KassenSync"; Filename: "{app}\App\{#MyAppExeName}"
Name: "{autodesktop}\KassenSync"; Filename: "{app}\App\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Desktop-Verknüpfung erstellen"; GroupDescription: "Zusätzliche Symbole:"; Flags: unchecked

[Run]
Filename: "{app}\Service\{#MyServiceExeName}"; Parameters: "--install-service"; Flags: runhidden waituntilterminated
Filename: "{app}\App\{#MyAppExeName}"; Description: "KassenSync starten"; Flags: nowait postinstall skipifsilent; Check: not IsUpdateInstall
Filename: "{app}\App\{#MyAppExeName}"; Parameters: "--updated"; Flags: nowait; Check: IsUpdateInstall

[UninstallRun]
Filename: "{app}\Service\{#MyServiceExeName}"; Parameters: "--uninstall-service"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveKassenSyncService"

[Code]
function IsUpdateInstall: Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
  begin
    if CompareText(ParamStr(I), '/UPDATE') = 0 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  OldServiceExe: String;
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    OldServiceExe := ExpandConstant('{app}\Service\{#MyServiceExeName}');
    if FileExists(OldServiceExe) then
    begin
      Exec(OldServiceExe, '--stop-service', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Sleep(1500);
    end;
  end;
end;
