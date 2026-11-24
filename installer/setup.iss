; =============================================================================
; Inno Setup Script — SysMonitor Widget (WinHardwareMonitor)
; Phiên bản tự động truyền từ WinHardwareMonitor.csproj qua /DMyAppVersion=x.y.z
; =============================================================================

#ifndef MyAppVersion
  #define MyAppVersion "1.0.1"
#endif

#define MyAppName "SysMonitor Widget"
#define MyAppDisplayName "Theo dõi phần cứng Windows 11"
#define MyAppPublisher "dangdinhnambhlc2709-alt"
#define MyAppURL "https://github.com/dangdinhnambhlc2709-alt/SysMonitor-Widget"
#define MyAppExeName "WinHardwareMonitor.exe"

[Setup]
AppId={{8F5E3B1D-4A2C-4E7A-9B3C-6F1D2E8A4C90}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/releases
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=output
OutputBaseFilename=SysMonitor-Widget-Setup-{#MyAppVersion}
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter=*.exe
RestartApplications=no
VersionInfoVersion={#MyAppVersion}.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\tools\PresentMon.exe"; DestDir: "{app}\tools"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\Gỡ cài đặt {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Mở ứng dụng sau khi cài đặt thủ công
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
; Tự động mở lại ứng dụng sau khi cập nhật ngầm (/SILENT) hoàn tất
Filename: "{app}\{#MyAppExeName}"; Flags: nowait skipifnotsilent

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    { Đảm bảo đóng sạch PresentMon nếu còn chạy nền trước khi ghi đè }
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM PresentMon.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
