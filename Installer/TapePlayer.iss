; Inno Setup script for Tape Cassette Player.
; Build with: dotnet publish "Tape Player.vbproj" -p:PublishProfile=win-x64-SelfContained
; then compile this script (see Installer\build.ps1 for a one-step wrapper).

#define MyAppName "Tape Cassette Player"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Brand"
#define MyAppExeName "Tape Player.exe"
#define PublishDir "..\publish\win-x64"

[Setup]
AppId={{B0FD7CBF-A868-42A7-86D6-7378DF28D49C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\TapeCassette.ico
Compression=lzma2
SolidCompression=yes
OutputDir=..\publish\installer
OutputBaseFilename=TapeCassettePlayerSetup
WizardStyle=modern
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
