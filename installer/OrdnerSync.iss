#define MyAppName "OrdnerSync"
#ifndef MyAppVersion
  #define MyAppVersion "0.3.3"
#endif
#define MyAppPublisher "OrdnerSync"
#define MyAppExeName "OrdnerSync.App.exe"
#define MyServiceExeName "OrdnerSync.Service.exe"
#define LegacyServiceExeName "KassenSync.Service.exe"

[Setup]
AppId={{9FC0E580-9938-4E06-A9A4-278689877167}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\OrdnerSync
UsePreviousAppDir=no
DefaultGroupName=OrdnerSync
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=OrdnerSync-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
SetupIconFile=..\src\KassenSync.App\Assets\OrdnerSync.ico
UninstallDisplayIcon={app}\App\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName=OrdnerSync
VersionInfoDescription=OrdnerSync Setup
SetupLogging=yes

[Files]
Source: "..\artifacts\publish\app\*"; DestDir: "{app}\App"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\publish\service\*"; DestDir: "{app}\Service"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: filesandordirs; Name: "{autopf}\KassenSync"

[Icons]
Name: "{autoprograms}\OrdnerSync"; Filename: "{app}\App\{#MyAppExeName}"
Name: "{autodesktop}\OrdnerSync"; Filename: "{app}\App\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Desktop-Verknüpfung erstellen"; GroupDescription: "Zusätzliche Symbole:"; Flags: unchecked

[Run]
Filename: "{app}\Service\{#MyServiceExeName}"; Parameters: "--install-service"; Flags: runhidden waituntilterminated
Filename: "{app}\App\{#MyAppExeName}"; Description: "OrdnerSync starten"; Flags: nowait postinstall skipifsilent; Check: not IsUpdateInstall
Filename: "{app}\App\{#MyAppExeName}"; Parameters: "--updated"; Flags: nowait; Check: IsUpdateInstall

[UninstallRun]
Filename: "{app}\Service\{#MyServiceExeName}"; Parameters: "--uninstall-service"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveOrdnerSyncService"

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

procedure StopExistingService(const ServiceExe: String);
var
  ResultCode: Integer;
begin
  if FileExists(ServiceExe) then
  begin
    Exec(ServiceExe, '--stop-service', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(1500);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    StopExistingService(ExpandConstant('{app}\Service\{#MyServiceExeName}'));
    StopExistingService(ExpandConstant('{autopf}\KassenSync\Service\{#LegacyServiceExeName}'));
  end;
end;
