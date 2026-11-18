#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SetupOutputName
  #define SetupOutputName "HoshiBar-Core-Setup-" + AppVersion
#endif
#ifndef AppSource
  #define AppSource "..\publish"
#endif
[Setup]
AppId={{7AD6B4D0-35EC-44DC-A0A9-E65887F8BFB5}
AppName=HoshiBar
AppVersion={#AppVersion}
AppPublisher=Takayom1
AppPublisherURL=https://github.com/Takayom1/hoshibar
AppSupportURL=https://github.com/Takayom1/hoshibar/issues
AppUpdatesURL=https://github.com/Takayom1/hoshibar/releases
DefaultDirName={localappdata}\Programs\HoshiBar
DefaultGroupName=HoshiBar
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\outputs
OutputBaseFilename={#SetupOutputName}
SetupIconFile=..\src\HoshiBar.ico
UninstallDisplayIcon={app}\HoshiBar.exe
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
LicenseFile=..\src\LICENSING.md
CloseApplications=yes
CloseApplicationsFilter=HoshiBar.exe,HoshinoIsland.exe,DIsland.exe
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
Type: files; Name: "{app}\HoshinoIsland.exe"
Type: files; Name: "{app}\HoshinoIsland.dll"
Type: files; Name: "{app}\HoshinoIsland.deps.json"
Type: files; Name: "{app}\HoshinoIsland.runtimeconfig.json"
Type: files; Name: "{app}\GlassDesk.ico"
Type: files; Name: "{app}\LICENSE-HOSHINO-MIT.txt"
Type: files; Name: "{app}\DIsland.exe"
Type: files; Name: "{app}\DIsland.dll"
Type: files; Name: "{app}\DIsland.deps.json"
Type: files; Name: "{app}\DIsland.runtimeconfig.json"
Type: files; Name: "{app}\.local-dev"
[Icons]
Name: "{group}\HoshiBar"; Filename: "{app}\HoshiBar.exe"
Name: "{group}\Настройки HoshiBar"; Filename: "{app}\HoshiBar.exe"; Parameters: "--settings"
Name: "{autodesktop}\HoshiBar"; Filename: "{app}\HoshiBar.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\HoshiBar.exe"; Description: "Запустить HoshiBar"; Flags: nowait postinstall skipifsilent
