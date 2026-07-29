# VeilBrowser 隐栈浏览器

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)
![Windows x64](https://img.shields.io/badge/Windows-10%2F11%20x64-0078D4)
[![Build](https://github.com/myHearDe/VeilBrowser/actions/workflows/build.yml/badge.svg)](https://github.com/myHearDe/VeilBrowser/actions/workflows/build.yml)

一款面向 Windows 10/11 x64 的本地隐私浏览器原型。界面使用 C# / WPF，
网页内核使用 Microsoft Edge WebView2（Chromium），项目面向
Visual Studio 2026 与 .NET 10 LTS。

**Keywords:** privacy browser, encrypted browser data, WPF browser,
WebView2 browser, tabbed browser, AdGuard, Windows desktop, C#, .NET 10.

## 当前版本能做什么

- 0.3.0 使用紧凑的 Fluent 深色界面，重做标签栏、地址栏、导航图标和状态栏。
- 迁移到 Edge WebView2，支持主流 H.264/AAC 视频、系统媒体解码和更完整的网站兼容性。
- 默认内置官方 AdGuard Browser Extension 5.4.3.1（Manifest V3）。
- 右上角 AdGuard 盾牌可打开官方控制面板；旁边菜单支持全局启停、
  自定义过滤规则、过滤日志和完整设置。
- 支持 AdGuard 广告/跟踪器拦截、过滤器订阅、白名单、用户规则、
  页面元素拦截、统计和配置导入导出等扩展自身能力。
- 多标签页、地址栏搜索、前进、后退、刷新、主页。
- 网页弹窗和右键“新窗口打开”统一进入当前窗口的新标签页。
- 标签页右键支持复制、关闭其他、关闭右侧；支持恢复关闭的标签页和 Ctrl+Tab 切换。
- 支持 F11/网页视频全屏、Ctrl+H 历史、Ctrl+J 下载、Ctrl+P 打印及常用导航快捷键。
- 下载、打开已下载文件、打印、页面查找、缩放、开发者工具。
- 浏览历史、下载记录、书签、密码保险库、会话记录。
- 可选启动主锁；历史、下载、书签、密码、Cookie/网站数据、会话、
  自动填充和设置入口可分别决定是否再次验证。
- 主密码使用 Argon2id（64 MiB、3 轮）派生解锁密钥。
- 浏览器实际数据使用独立随机 256 位主密钥。
- 主密钥由 AES-256-GCM 包裹；敏感状态使用 AES-256-GCM 加密。
- 整个 WebView2 配置目录在正常关闭后进入分块 AES-256-GCM 加密容器。
- 不设置浏览器密码时，随机主密钥仍由 Windows DPAPI 当前账户保护。
- 闲置自动锁定、手动立即锁定、紧急退出并清除。
- HTTPS 升级、第三方 Cookie 限制、WebRTC 非代理 UDP 限制。
- 安装版提供开始菜单卸载入口；卸载时可选择保留或彻底删除本地浏览数据。
- 阻止多实例同时打开同一加密资料目录，避免配置竞争和损坏。
- Cookie 与网站数据可以在资料中心安排清理，并在安全关闭时执行。

## AdGuard 操作

- 点击地址栏右侧绿色盾牌：打开 AdGuard 当前页面控制面板。
- 点击盾牌右侧 `ON/OFF`：暂停或恢复全部防护，也可打开自定义规则、
  过滤日志和完整设置。
- AdGuard 首次安装会打开一次官方欢迎页；之后设置、规则和统计数据都保存在
  WebView2 配置目录中，并随浏览器资料一起加密。
- 内置扩展位于 `Extensions\AdGuard`，不能只复制主 EXE。

## 必须理解的安全边界

WebView2 运行时必须读取 Cookie、缓存和网站数据。因此浏览器解锁运行期间，
工作配置目录是可读的；正常关闭或安全锁定后才会重新加密。该设计能防止他人
直接复制关闭状态下的浏览器目录读取记录，但不能防御已经取得 Windows 管理员
权限、能读取进程内存、安装键盘记录器或在浏览器运行时访问临时目录的攻击者。

异常断电或强制结束进程可能留下工作目录。下次正常启动会在拿到正确密钥后先
回收并重新加密该目录。SSD 的磨损均衡也意味着“覆盖删除”不能被宣传为物理层面
绝对不可恢复。

## 构建

最简单的方法见 [BUILDING.zh-CN.md](BUILDING.zh-CN.md)。

首次构建会从 AdGuard 官方 GitHub Release 下载固定版本的 MV3 扩展和对应源码，
并校验 SHA-256；大型第三方产物不会重复存入本源码仓库。

```powershell
.\scripts\build.ps1
```

发布免安装版：

```powershell
.\scripts\publish.ps1
```

输出位于 `artifacts\VeilBrowser-win-x64`，同时生成 ZIP。

### 一键打包新版

1. 在 `src\VeilBrowser\VeilBrowser.csproj` 修改 `<Version>`；
2. 双击根目录的 `一键打包新版.cmd`；
3. 从 `artifacts\installer` 取得带新版本号的安装包和 SHA-256 文件。

命令行方式（首次会自动安装 Inno Setup 6）：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1 -InstallCompiler
```

安装包输出到 `artifacts\installer`。完整参数和静默安装/卸载方式见
`installer\README.zh-CN.md`。

## 卸载与本地数据清理

安装版可以从浏览器右上角“更多 → 卸载或清理本机数据”、Windows“已安装的应用”
或开始菜单的“卸载 VeilBrowser 隐栈浏览器”进入卸载程序。浏览器内发起卸载时，
程序会先正常保存并加密资料后再关闭。卸载时会询问：

- 选择“否”：只移除程序，保留 `%LocalAppData%\VeilBrowser` 中的加密资料，
  以后重新安装仍可继续使用。
- 选择“是”：同时删除历史、书签、密码库、Cookie、缓存、加密容器、安全设置
  和临时工作目录。此操作无法撤销。

无论选择哪项，都不会删除 Windows 普通“下载”文件夹里的已下载文件。
免安装版可在浏览器完全退出后运行随包附带的 `Clean-Local-Data.ps1` 清理本机资料。

## 项目结构

- `src/VeilBrowser.Core`：加密、配置容器和数据模型。
- `src/VeilBrowser`：WPF 界面、WebView2 浏览器外壳、扩展管理和安全入口。
- `third_party`：第三方依赖说明；AdGuard 构建输入由校验哈希的脚本下载。
- `tests/VeilBrowser.Core.SmokeTests`：不依赖测试框架的加密往返测试。
- `installer`：Inno Setup 安装脚本。
- `docs/THREAT-MODEL.zh-CN.md`：威胁模型与剩余风险。

## 当前不是完整 Edge

这是基于系统 Edge WebView2 Runtime 的浏览器外壳，不承诺 Microsoft 账号同步、
Edge 扩展商店、完整内置翻译、企业策略或全部 `edge://` 页面。除内置 AdGuard
外没有通用扩展商店界面。DRM、HEVC 等能力仍取决于系统 WebView2 Runtime、
Windows 媒体组件、显卡驱动和目标网站策略。

## 许可证

本项目自身源码使用 MIT License。内置 AdGuard Browser Extension 是独立第三方组件，
使用 GNU GPLv3；Microsoft WebView2 Runtime、Chromium 及其他第三方组件保留各自
许可证。构建产物中的 `Extensions\AdGuard\LICENSE`、`ThirdPartySource` 和其他
许可证文件不得删除。
