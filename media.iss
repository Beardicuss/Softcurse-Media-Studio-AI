; Inno Setup Script for Softcurse Media Lab AI
; Updated: 2026-03-12 — v3.0 (Toolkit Lab, Forge Lab, namespace cleanup, multi-size icon)

#define MyAppName "Softcurse Media Lab AI"
#define MyAppVersion "3.0"
#define MyAppPublisher "Softcurse"
#define MyAppURL "https://github.com/Beardicuss/Softcurse-Media-Studio-AI"
#define MyAppExeName "SoftcurseMediaLabAI.exe"

[Setup]
AppId={{A4F7B9E2-3C1D-4E5A-8F6B-9D0E1F2A3B4C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=
OutputDir=installer
OutputBaseFilename=SoftcurseMediaLabAI_Setup_v{#MyAppVersion}
SetupIconFile=assets\media.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Main application binaries and dependencies
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.lib,*.obj"

; ONNX models and configs (shipped with installer if present)
Source: "gui\models\*.onnx"; DestDir: "{app}\models"; Flags: ignoreversion skipifsourcedoesntexist
Source: "gui\models\*.yaml"; DestDir: "{app}\models"; Flags: ignoreversion skipifsourcedoesntexist

; Application icon
Source: "assets\media.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\media.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\media.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
