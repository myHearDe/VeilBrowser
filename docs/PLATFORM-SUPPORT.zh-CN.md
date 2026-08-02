# 平台与浏览器内核支持策略

## 结论

VeilBrowser 的正式维护通道支持 **Windows 10 22H2 x64** 和
**Windows 11 x64**，使用 Microsoft Edge WebView2 Evergreen Runtime。

无法同时满足以下两个条件：

1. 在 Windows 7 上运行；
2. 使用仍在获得 Chromium 安全更新的现代 WebView2 内核。

Microsoft Edge 109 是 Windows 7/8.1 的最后版本，已于 2023 年结束支持；
当前 WebView2 支持的 Windows 客户端为 Windows 10/11。当前项目使用的 .NET 10
也不支持 Windows 7。因此项目不会把停更内核包装成“安全的 Win7 支持”。

官方依据：

- [WebView2 支持的平台](https://learn.microsoft.com/en-us/microsoft-edge/webview2/)
- [Microsoft Edge 生命周期（Windows 7/8.1 最后版本为 109）](https://learn.microsoft.com/en-us/lifecycle/products/microsoft-edge)
- [.NET 在 Windows 上的支持矩阵](https://learn.microsoft.com/en-gb/dotnet/core/install/windows?tabs=net70)

## 支持矩阵

| 系统 | 状态 | 内核策略 | AdGuard |
|---|---|---|---|
| Windows 11 x64 | 正式支持 | Evergreen WebView2，随系统自动更新 | 5.4.3.1，要求 Chromium 121+ |
| Windows 10 22H2 x64 | 正式支持 | Evergreen WebView2 | 5.4.3.1，要求 Chromium 121+ |
| 更早的 Windows 10 | 不在正式测试范围 | .NET 10 不保证支持 | 不保证 |
| Windows 7 / 8.1 | 不支持 | 只能停留在已结束支持的 Chromium 109 | 当前内置版本无法运行 |

## 内核不过时的维护方式

- 应用使用 Evergreen WebView2，而不是把固定 Chromium 内核复制进安装包。
- 启动 AdGuard 前检查运行时主版本；低于 Chromium 121 时明确报错，不静默失效。
- GitHub Dependabot 每周检查 NuGet（包括 WebView2 SDK）更新。
- 每次发布在 Windows 10 22H2 与 Windows 11 上验证：
  - WebView2 Runtime 版本；
  - H.264/AAC 视频播放；
  - WebRTC、WebSocket、下载和全屏；
  - AdGuard 安装、当前网站开关、元素选择器、用户规则和过滤日志。

注意：WebView2 **SDK NuGet 版本**与用户机器上的**浏览器 Runtime 版本**不是一回事。
SDK 通过依赖更新维护 API，Evergreen Runtime 负责持续获得 Chromium 更新。

## 如果必须给 Windows 7 用户提供版本

只能另建明确标记为 `Legacy / Unsupported` 的分支，采用 .NET Framework 4.8 和
Chromium/WebView2 109 或其他同代内核，并同时做到：

- 与现代版使用不同安装包、更新源和用户数据目录；
- 禁止处理账号、支付和其他敏感网站；
- 不宣称安全支持；
- 不让现代版为兼容旧内核而停止升级。

该方案能“运行”，但不能满足“内核不能太老”的要求，因此不作为正式发布通道。
