#define MyAppName "VeilBrowser 隐栈浏览器"
#ifndef MyAppVersion
  #define MyAppVersion "0.3.0"
#endif
#ifndef MyAppVersionInfo
  #define MyAppVersionInfo "0.3.0.0"
#endif
#define MyAppPublisher "myHearDe"
#define MyAppExeName "VeilBrowser.exe"
#define MyAppMutex "Local\VeilBrowser.SingleInstance"

[Setup]
AppId={{4EF1BEA4-7BA9-4CB7-9DFE-6432A954B823}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\VeilBrowser
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes
LicenseFile=..\LICENSE
OutputDir=..\artifacts\installer
OutputBaseFilename=VeilBrowser-Setup-{#MyAppVersion}-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
AppMutex={#MyAppMutex}
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no
SetupLogging=yes
UsePreviousAppDir=yes
Uninstallable=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersionInfo}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} 安装向导
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoOriginalFileName=VeilBrowser-Setup-{#MyAppVersion}-x64.exe

[Languages]
Name: "chinesesimp"; MessagesFile: "Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked

[Files]
Source: "..\artifacts\VeilBrowser-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\{#MyAppExeName}"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\{#MyAppExeName}"; ValueType: string; ValueName: "Path"; ValueData: "{app}"; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "安装完成后启动 {#MyAppName}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DeleteBrowserData: Boolean;

function HasUninstallParameter(const Name: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
  begin
    if CompareText(ParamStr(I), Name) = 0 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function ConfirmBrowserDataRemoval: Boolean;
begin
  Result :=
    MsgBox(
      '是否同时删除本机的全部隐栈浏览器数据？' + #13#10 + #13#10 +
      '将删除：浏览历史、书签、密码库、Cookie、缓存、加密容器、安全设置和临时工作目录。' + #13#10 + #13#10 +
      '普通“下载”文件夹中的已下载文件不会被删除。选择“否”只卸载程序并保留数据。',
      mbConfirmation, MB_YESNO) = IDYES;

  if Result then
  begin
    Result :=
      MsgBox(
        '请再次确认：浏览器数据删除后无法恢复。' + #13#10 + #13#10 +
        '确定要继续彻底删除吗？',
        mbConfirmation, MB_YESNO) = IDYES;
  end;
end;

function InitializeUninstall: Boolean;
begin
  Result := True;
  DeleteBrowserData := HasUninstallParameter('/PURGEDATA');

  if not DeleteBrowserData and not UninstallSilent then
  begin
    DeleteBrowserData := ConfirmBrowserDataRemoval;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and DeleteBrowserData then
  begin
    DelTree(ExpandConstant('{localappdata}\VeilBrowser'), True, True, True);
    DelTree(ExpandConstant('{localappdata}\Temp\VeilBrowser'), True, True, True);
  end;
end;
