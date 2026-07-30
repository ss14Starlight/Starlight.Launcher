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
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
CloseApplicationsFilter=*.exe

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs
Source: "MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\{#AppName}";        Filename: "{app}\{#ExeName}"
Name: "{autodesktop}\{#AppName}";  Filename: "{app}\{#ExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Installing Microsoft Edge WebView2 Runtime..."
Filename: "{app}\{#ExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Best-effort - make sure nothing's left running when uninstalling, mirroring
; CloseApplications on install.
Filename: "{cmd}"; Parameters: "/C taskkill /IM ""{#ExeName}"" /F"; Flags: runhidden skipifdoesntexist; RunOnceId: "KillOnUninstall"
