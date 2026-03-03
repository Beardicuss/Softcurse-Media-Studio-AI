; Inno Setup Script for Softcurse Media Studio AI
; Created: 2026-03-03

#define MyAppName "Softcurse Media Studio AI"
#define MyAppVersion "2.8"
#define MyAppPublisher "Softcurse"
#define MyAppURL "https://github.com/Beardicuss/Softcurse-Media-Studio-AI"
#define MyAppExeName "SoftcurseMediaStudioAI.exe"

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
OutputBaseFilename=SoftcurseMediaStudioAI_Setup_v{#MyAppVersion}
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
; Main application executable
Source: "publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

; Runtime DLLs
Source: "publish\DirectML.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\DirectML.Debug.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\onnxruntime.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\onnxruntime_providers_shared.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\OpenCvSharpExtern.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\opencv_videoio_ffmpeg490_64.dll"; DestDir: "{app}"; Flags: ignoreversion

; ONNX models (shipped with installer)
Source: "gui\models\*.onnx"; DestDir: "{app}\models"; Flags: ignoreversion; Check: ModelExists

; Application icon
Source: "assets\media.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\media.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\media.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function ModelExists: Boolean;
begin
  Result := FileExists(ExpandConstant('{src}\gui\models\lama_fp32.onnx'));
end;
