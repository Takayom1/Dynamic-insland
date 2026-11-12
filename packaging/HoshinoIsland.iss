#define AppVersion "0.15.0"
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
UninstallDisplayIcon={app}\DIsland.exe
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
LicenseFile=..\src\LICENSING.md
CloseApplications=yes
CloseApplicationsFilter=DIsland.exe
RestartApplications=no
MinVersion=10.0.19041
[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; Flags: unchecked
[Files]
Source: "{#AppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Hoshino Island"; Filename: "{app}\DIsland.exe"
Name: "{group}\Настройки Hoshino Island"; Filename: "{app}\DIsland.exe"; Parameters: "--settings"
Name: "{autodesktop}\Hoshino Island"; Filename: "{app}\DIsland.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\DIsland.exe"; Description: "Запустить Hoshino Island"; Flags: nowait postinstall skipifsilent
