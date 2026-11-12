#ifndef AppVersion
  #define AppVersion "0.17.0"
#endif
#ifndef SetupOutputName
  #define SetupOutputName "Hoshino-Island-Core-Setup-" + AppVersion
#endif
#ifndef AppSource
  #define AppSource "..\publish"
#endif
[Setup]
AppId={{7AD6B4D0-35EC-44DC-A0A9-E65887F8BFB5}
AppName=Hoshino Island
AppVersion={#AppVersion}
AppPublisher=Takayom1
AppPublisherURL=https://github.com/Takayom1/Dynamic-insland
AppSupportURL=https://github.com/Takayom1/Dynamic-insland/issues
AppUpdatesURL=https://github.com/Takayom1/Dynamic-insland/releases
DefaultDirName={localappdata}\Programs\HoshinoIsland
DefaultGroupName=Hoshino Island
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\outputs
OutputBaseFilename={#SetupOutputName}
SetupIconFile=..\src\GlassDesk.ico
UninstallDisplayIcon={app}\HoshinoIsland.exe
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
LicenseFile=..\src\LICENSING.md
CloseApplications=yes
CloseApplicationsFilter=HoshinoIsland.exe,DIsland.exe
RestartApplications=no
MinVersion=10.0.19041
[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; Flags: unchecked
[Files]
Source: "{#AppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[InstallDelete]
Type: files; Name: "{app}\DIsland.exe"
Type: files; Name: "{app}\DIsland.dll"
Type: files; Name: "{app}\DIsland.deps.json"
Type: files; Name: "{app}\DIsland.runtimeconfig.json"
Type: files; Name: "{app}\.local-dev"
[Icons]
Name: "{group}\Hoshino Island"; Filename: "{app}\HoshinoIsland.exe"
Name: "{group}\Настройки Hoshino Island"; Filename: "{app}\HoshinoIsland.exe"; Parameters: "--settings"
Name: "{autodesktop}\Hoshino Island"; Filename: "{app}\HoshinoIsland.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\HoshinoIsland.exe"; Description: "Запустить Hoshino Island"; Flags: nowait postinstall skipifsilent
