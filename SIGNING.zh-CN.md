# VeilBrowser 代码签名

## 当前一键脚本：人工签名模式

直接双击：

```text
一键打包新版.cmd
```

脚本会执行：

1. 重新发布最终程序；
2. 自动打开发布目录并暂停；
3. 等你手工签名三个程序文件；
4. 在控制台输入 `CONTINUE` 或 `继续`；
5. 验证三个签名、时间戳及签名证书是否一致；
6. 用已签名文件重建免安装 ZIP；
7. 编译安装包并自动打开安装包所在目录；
8. 等你手工签名安装包；
9. 再次输入 `CONTINUE` 或 `继续`；
10. 验证安装包签名和时间戳，生成最终 SHA-256 与签名报告。

输入 `CANCEL` 或 `退出` 可以安全终止。未通过签名验证时不会继续生成正式结果。

需要手工签名的第一组文件：

```text
artifacts\VeilBrowser-win-x64\VeilBrowser.exe
artifacts\VeilBrowser-win-x64\VeilBrowser.dll
artifacts\VeilBrowser-win-x64\VeilBrowser.Core.dll
```

第二次需要签名：

```text
artifacts\installer\VeilBrowser-Setup-<版本>-x64.exe
```

## 正确的发布顺序

代码签名必须接在正式发布之后、安装器编译之前：

1. `dotnet publish` 生成最终程序文件；
2. 签名并验证 `VeilBrowser.exe`、`VeilBrowser.dll`、`VeilBrowser.Core.dll`；
3. 用已签名文件重新生成免安装 ZIP；
4. Inno Setup 把已签名程序装入安装包；
5. 再对最终安装包签名并验证；
6. 最后生成 SHA-256 文件。

不能先签名 `bin` 目录再双击普通打包脚本，因为重新执行 `dotnet publish` 会生成
新文件并覆盖之前的签名。

## 准备工作

需要：

- 受信任 CA 签发的 Authenticode 代码签名证书；
- Windows SDK 的 **Signing Tools for Desktop Apps** 组件，即 `signtool.exe`；
- 可访问的 RFC 3161 时间戳服务器。

自签名证书可以用于内部测试，但其他电脑默认不会信任，不能解决
“未知发布者”或 SmartScreen 信誉问题。

## 可选：让脚本自动调用 SignTool

如果以后不再需要人工暂停，也可以直接调用 `scripts\build-installer.ps1 -Sign`。

### 方法 A：PFX 证书

复制本地配置模板：

```powershell
Copy-Item .\signing.example.ps1 .\signing.local.ps1
```

编辑 `signing.local.ps1`：

```powershell
@{
    PfxPath = "D:\Certificates\my-code-signing-certificate.pfx"
    CertificateThumbprint = ""
    CertificateStoreLocation = "CurrentUser"
    TimestampUrl = "http://timestamp.digicert.com"
}
```

PFX 应存放在项目目录之外。不要把密码写进配置文件。双击一键打包脚本后，
程序会在终端中安全提示输入 PFX 密码。

### 方法 B：证书存储区、USB Key 或云签名客户端

先查找带私钥的代码签名证书：

```powershell
Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Select-Object Subject, Thumbprint, NotAfter, HasPrivateKey
```

然后配置：

```powershell
@{
    PfxPath = ""
    CertificateThumbprint = "在这里填写40位证书指纹"
    CertificateStoreLocation = "CurrentUser"
    TimestampUrl = "http://timestamp.digicert.com"
}
```

如果证书在本机计算机存储区，将 `CertificateStoreLocation` 改为
`LocalMachine`。USB Key、Certum 或其他硬件证书可能在签名时另外弹出 PIN 窗口。

### 自动签名并打包

配置好 `signing.local.ps1` 后，直接运行：

```powershell
.\scripts\build-installer.ps1 -InstallCompiler
```

命令行方式：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
    -File .\scripts\build-installer.ps1 -InstallCompiler -Sign `
    -PfxPath "D:\Certificates\my-code-signing-certificate.pfx"
```

也可以使用环境变量：

```powershell
$env:VEIL_SIGN_PFX = "D:\Certificates\my-code-signing-certificate.pfx"
$env:VEIL_SIGN_TIMESTAMP_URL = "http://timestamp.digicert.com"
.\scripts\build-installer.ps1 -Sign
```

不要永久保存 `VEIL_SIGN_PFX_PASSWORD`。如果没有设置密码环境变量，脚本会交互式
提示输入密码。

## 分步手工签名

如果使用 CA 专用客户端手工签名：

```powershell
.\scripts\publish.ps1
```

签名以下三个最终发布文件：

```text
artifacts\VeilBrowser-win-x64\VeilBrowser.exe
artifacts\VeilBrowser-win-x64\VeilBrowser.dll
artifacts\VeilBrowser-win-x64\VeilBrowser.Core.dll
```

然后只执行安装器编译，避免重新发布覆盖签名：

```powershell
.\scripts\build-installer.ps1 -SkipPublish
```

最后还要使用同一证书签名：

```text
artifacts\installer\VeilBrowser-Setup-<版本>-x64.exe
```

## 验证

```powershell
Get-AuthenticodeSignature .\artifacts\VeilBrowser-win-x64\VeilBrowser.exe |
    Format-List Status, StatusMessage, SignerCertificate, TimeStamperCertificate

Get-AuthenticodeSignature .\artifacts\installer\VeilBrowser-Setup-0.3.0-x64.exe |
    Format-List Status, StatusMessage, SignerCertificate, TimeStamperCertificate
```

脚本还会执行：

```powershell
signtool verify /pa /v <文件>
```

任何签名或验证失败都会立即停止打包，不会把失败产物当成正式版本。
