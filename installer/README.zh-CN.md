# VeilBrowser 安装与卸载向导

项目使用 [Inno Setup 6](https://jrsoftware.org/isinfo.php) 生成中文安装向导和卸载向导。

## 一键生成安装包

首次构建（自动通过 winget 安装 Inno Setup）：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1 -InstallCompiler
```

以后构建：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
```

如果便携版目录已经是最新的，可跳过 `dotnet publish`：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1 -SkipPublish
```

输出文件：

- `artifacts\installer\VeilBrowser-Setup-<版本>-x64.exe`
- `artifacts\installer\VeilBrowser-Setup-<版本>-x64.exe.sha256.txt`

版本号自动读取 `src\VeilBrowser\VeilBrowser.csproj` 的 `<Version>`。

## 安装行为

- 中文现代安装向导。
- 当前用户安装到 `%LOCALAPPDATA%\Programs\VeilBrowser`，无需管理员权限。
- 可选创建桌面快捷方式。
- 创建开始菜单快捷方式和卸载入口。
- 注册当前用户的 `App Paths`，便于 Windows 定位 `VeilBrowser.exe`。
- 检测正在运行的 VeilBrowser，避免覆盖使用中的程序文件。
- 安装完成后可直接启动浏览器。
- 使用固定 `AppId`，再次运行新版本安装包时执行原位升级。

## 卸载行为

可从以下任一入口打开卸载向导：

1. Windows“设置 → 应用 → 已安装的应用 → VeilBrowser 隐栈浏览器 → 卸载”；
2. 开始菜单中的“卸载 VeilBrowser 隐栈浏览器”；
3. 安装目录中的 `unins000.exe`。

交互卸载会询问是否删除浏览器数据，并在选择删除后进行第二次确认：

- 选择“否”：只删除程序，保留书签、历史、密码库、Cookie、缓存和设置；
- 选择“是”并再次确认：额外删除 `%LOCALAPPDATA%\VeilBrowser` 和 `%LOCALAPPDATA%\Temp\VeilBrowser`；
- 普通“下载”文件夹中的文件不会被删除。

## 静默安装与卸载

静默安装：

```powershell
.\VeilBrowser-Setup-0.3.0-x64.exe /VERYSILENT /NORESTART
```

静默卸载并保留用户数据：

```powershell
& "$env:LOCALAPPDATA\Programs\VeilBrowser\unins000.exe" /VERYSILENT /NORESTART
```

静默卸载并彻底清理用户数据：

```powershell
& "$env:LOCALAPPDATA\Programs\VeilBrowser\unins000.exe" /VERYSILENT /NORESTART /PURGEDATA
```

## 主要配置文件

- 安装器定义：`installer\VeilBrowser.iss`
- 简体中文语言文件：`installer\Languages\ChineseSimplified.isl`
- 构建入口：`scripts\build-installer.ps1`
- 便携版发布：`scripts\publish.ps1`
