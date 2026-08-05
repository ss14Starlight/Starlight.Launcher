[Setup]
AppId={{7168EE69-1ED3-458C-AE24-A189E255112D}}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Starlight Team
AppPublisherURL=https://github.com/ss14Starlight/Starlight.Launcher
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=Output
OutputBaseFilename={#AppName}-win-x64-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern

UninstallDisplayIcon={app}\{#ExeName}
VersionInfoVersion={#AppVersion}

; AppMutex=Global\StarlightLauncher
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedsWebView2

[Icons]
Name: "{group}\{#AppName}";            Filename: "{app}\{#ExeName}"
Name: "{group}\Uninstall {#AppName}";  Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";      Filename: "{app}\{#ExeName}"; Tasks: desktopicon

[Run]
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; \
  StatusMsg: "Installing Microsoft Edge WebView2 Runtime..."; Check: NeedsWebView2
Filename: "{app}\{#ExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM ""{#ExeName}"" /F"; \
  Flags: runhidden; RunOnceId: "KillOnUninstall"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
const
  WV2_CLIENT = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

function WV2Pv(RootKey: Integer; const SubKey: String): String;
begin
  if not RegQueryStringValue(RootKey, SubKey, 'pv', Result) then
    Result := '';
end;

function NeedsWebView2: Boolean;
var
  V: String;
begin
  V := WV2Pv(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\' + WV2_CLIENT);
  if V = '' then
    V := WV2Pv(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\' + WV2_CLIENT);
  if V = '' then
    V := WV2Pv(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\' + WV2_CLIENT);
  Result := (V = '') or (V = '0.0.0.0');
end;

procedure CleanPreviousInstall(const Dir: String);
var
  FR: TFindRec;
  Path: String;
begin
  if FindFirst(Dir + '\*', FR) then
  try
    repeat
      if (FR.Name = '.') or (FR.Name = '..') then
        Continue;
      if Copy(Lowercase(FR.Name), 1, 5) = 'unins' then
        Continue;
      Path := Dir + '\' + FR.Name;
      if (FR.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        DelTree(Path, True, True, True)
      else
        DeleteFile(Path);
    until not FindNext(FR);
  finally
    FindClose(FR);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  AppDir: String;
begin
  if CurStep = ssInstall then
  begin
    AppDir := ExpandConstant('{app}');
    if FileExists(AppDir + '\{#ExeName}') then
      CleanPreviousInstall(AppDir);
  end;
end;
