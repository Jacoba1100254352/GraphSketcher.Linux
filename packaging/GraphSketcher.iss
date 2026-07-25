#define MyAppName "GraphSketcher"
#define MyAppPublisher "GraphSketcher Windows contributors"
#define MyAppExeName "GraphSketcher.exe"
#define MyAppUrl "https://github.com/Jacoba1100254352/GraphSketcher.Windows"

#ifndef MyAppVersion
  #define MyAppVersion "0.1.0-dev"
#endif

#ifndef SourceDir
  #define SourceDir "..\artifacts\GraphSketcher-Windows-win-x64"
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

#define MyAppBinary AddBackslash(SourceDir) + MyAppExeName

[Setup]
AppId={{A6331E74-A416-4DA9-B9B4-388DDF3B98FE}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
ChangesAssociations=yes
CloseApplications=yes
Compression=lzma2
DefaultDirName={localappdata}\Programs\GraphSketcher
DefaultGroupName=GraphSketcher
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
MinVersion=10.0.17763
OutputBaseFilename=GraphSketcher-Windows-v{#MyAppVersion}-win-x64-Setup
OutputDir={#OutputDir}
PrivilegesRequired=lowest
RestartApplications=no
SetupIconFile=..\src\GraphSketcher.App\Assets\GraphSketcher.ico
SolidCompression=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
UsePreviousAppDir=yes
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=GraphSketcher for Windows installer
VersionInfoProductName={#MyAppName}
VersionInfoProductTextVersion={#MyAppVersion}
VersionInfoTextVersion={#MyAppVersion}
VersionInfoVersion={#GetVersionNumbersString(MyAppBinary)}
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\GraphSketcher"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\GraphSketcher"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\.graphsketch"; ValueType: string; ValueName: ""; ValueData: "GraphSketcher.Document"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\GraphSketcher.Document"; ValueType: string; ValueName: ""; ValueData: "GraphSketcher graph"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\GraphSketcher.Document\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKCU; Subkey: "Software\Classes\GraphSketcher.Document\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch GraphSketcher"; Flags: nowait postinstall skipifsilent
