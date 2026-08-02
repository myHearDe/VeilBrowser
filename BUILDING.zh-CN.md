# 用 Visual Studio 2026 编译

## 1. 安装一次开发环境

打开 **Visual Studio Installer**，修改 Visual Studio 2026，勾选：

1. **.NET 桌面开发** 工作负载。
2. 单个组件中的 **.NET 10 SDK**。
3. 建议安装 **Git for Windows**。

程序只构建 x64，不需要 Python、Node.js、完整 Chromium 源码或 C++ 工作负载。
首次还原 NuGet 包时需要网络。Windows 10/11 目标电脑还需要 Edge WebView2 Runtime。

## 2. 在 VS2026 中构建

1. 解压源码到纯英文或普通中文路径，不要放到系统保护目录。
2. 双击根目录的 `VeilBrowser.slnx`。
3. 等待右下角 NuGet 还原结束。
4. 工具栏配置选择 `Release`，平台选择 `x64`。
5. 菜单选择 **生成 → 生成解决方案**。
6. 按 `F5` 调试，或按 `Ctrl+F5` 直接运行。

可执行文件在：

```text
src\VeilBrowser\bin\Release\net10.0-windows\win-x64\
```

不要只复制 `VeilBrowser.exe`。必须保留输出目录中的 .NET 组件、
`WebView2Loader.dll` 和 `Extensions\AdGuard` 整个目录。

## 3. 一键构建

在源码根目录右键“在终端中打开”，运行：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\build.ps1
```

脚本会依次还原包、编译 Release，并运行加密核心冒烟测试。
首次运行时还会从 AdGuard 官方 GitHub Release 下载固定版本的 MV3 扩展和对应
源码包，并校验 SHA-256。也可单独运行：

```powershell
.\scripts\setup-adguard.ps1
```

## 4. 生成可发给别人的免安装 ZIP

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\publish.ps1
```

输出：

```text
artifacts\VeilBrowser-win-x64\
artifacts\VeilBrowser-win-x64.zip
```

发布使用 self-contained .NET 运行时，所以目标电脑不必另装 .NET。WebView2
Runtime 通常随 Windows 10/11 和 Microsoft Edge 安装。

## 5. 一键生成安装包

每次发布新版时：

1. 修改 `src\VeilBrowser\VeilBrowser.csproj` 中的 `<Version>`；
2. 双击根目录的 `一键打包新版.cmd`；
3. 等待还原、测试、发布和安装器编译完成。

安装包和 SHA-256 文件输出到 `artifacts\installer`。脚本会在首次运行时通过
winget 安装 Inno Setup 6，之后自动复用。

命令行等效操作：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1 -InstallCompiler
```

正式发布前的 Authenticode 签名流程见 `SIGNING.zh-CN.md`。双击
`一键打包新版.cmd` 后，脚本会在程序发布完成和安装包生成完成两个位置暂停，
等待人工签名和 `CONTINUE` 指令；签名或时间戳验证失败时立即停止。

安装完成后，Windows“已安装的应用”和开始菜单里都会出现卸载入口。卸载程序会
询问是保留加密浏览资料，还是连同 `%LocalAppData%\VeilBrowser` 与临时工作目录
一起彻底删除。默认选择“保留”，防止误删密码库和书签。

自动化静默卸载默认也保留资料；只有显式添加 `/PURGEDATA` 才会清理：

```powershell
& "$env:LOCALAPPDATA\Programs\VeilBrowser\unins000.exe" /VERYSILENT /PURGEDATA
```

实际安装目录可能不同，请以 Windows 卸载项记录的路径为准。

## 常见问题

### 提示找不到 .NET 10

回到 Visual Studio Installer，安装“.NET 桌面开发”和“.NET 10 SDK”，然后重启
Visual Studio。

### 启动时报缺少 WebView2 Runtime 或 `WebView2Loader.dll`

请先安装 Microsoft Edge WebView2 Evergreen Runtime，并使用 `publish.ps1`
生成的整个目录或 ZIP，不要只复制 EXE。

### 被杀毒软件提示

未签名的新程序可能触发信誉提示。正式分发前应购买代码签名证书并给 EXE 和安装
包签名。不要关闭 WebView2 或 Windows 安全机制来绕过问题。

### 强制结束后发现临时目录

再次正常启动并输入正确主密码，程序会先回收遗留工作目录；随后正常退出，让它
完成加密。不要在“正在加密浏览器资料”时结束进程。
