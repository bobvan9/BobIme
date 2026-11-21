; Bob 五笔 安装脚本：先跑 installer/build.sh 产出 artifacts\publish，再用 ISCC 编译本文件。
#define MyAppName "Bob 五笔"
#define MyAppVersion "1.1.2"
#define MyAppPublisher "bob"
#define MyAppExeName "BobIme.Shell.exe"
#define PublishDir "..\artifacts\publish"

[Setup]
; AppId 一旦发布就不能改，升级安装靠它认同一个产品。
AppId={{7A3C1E58-2F4B-4D9A-8C6E-B0B1A7E5D310}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
; 装到公共 Program Files 就得写系统目录，这里按全用户安装提权，UAC 会弹一次。
PrivilegesRequired=admin
; [Registry] 里清残留 HKCU Run 项是故意的（提权用的还是同一个账户的 HKCU），所以关掉这条通用告警。
UsedUserAreasWarning=no
DefaultDirName={commonpf}\BobIme
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=BobIme-Setup-{#MyAppVersion}
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll
SetupLogging=yes

[Languages]
; Inno 6.7 没自带简体中文语言包，这里以英文为底，把看得见的几条改成中文。
Name: "zh"; MessagesFile: "compiler:Default.isl"

[Messages]
SetupAppTitle={#MyAppName} 安装向导
UninstallAppTitle={#MyAppName} 卸载程序
WelcomeLabel1=欢迎使用 {#MyAppName} 安装向导
WelcomeLabel2=本向导会把 {#MyAppName} {#MyAppVersion} 安装到你的电脑。%n%n建议先关闭正在运行的输入法程序再继续。
SelectDirLabel3=安装程序将把 {#MyAppName} 放到以下文件夹。
SelectDirBrowseLabel=点击「下一步」继续，点击「浏览」换一个文件夹。
FinishedLabel={#MyAppName} 已安装完成。
FinishedLabelNoIcons={#MyAppName} 已安装完成。
ClickFinish=点击「完成」退出安装向导。
ButtonNext=下一步(&N) >
ButtonBack=< 上一步(&B)
ButtonFinish=完成(&F)
ButtonCancel=取消

[Tasks]
Name: "autostart"; Description: "开机后自动启动 {#MyAppName}"; GroupDescription: "启动方式："
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："

[Files]
; 自包含发布里带的是 .NET 运行时，pdb 对用户没用，打包时排掉。
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon

[Registry]
; 全用户安装就写 HKLM 的 Run 项，登录后对所有用户都生效；HKLM64 是 Windows 实际读的那一份视图。
Root: HKLM64; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
  ValueName: "BobIme"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart
; 1.0.0 之前是按用户安装的，残留的 HKCU Run 项会在登录后多拉一份，导致两个钩子同时挂上，这里清掉。
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; \
  ValueName: "BobIme"; Flags: deletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; 卸载器删文件前得先把常驻进程收掉，否则 exe 被占用删不干净。
Filename: "taskkill.exe"; Parameters: "/f /im {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillIme"
