# VeilBrowser 安全与质量审查

审查日期：2026-07-31
审查范围：`src/VeilBrowser`、`src/VeilBrowser.Core`、安装与构建脚本、AdGuard 集成、Core/UI 冒烟测试。

## 结论

本轮已修复发现的高风险数据保护问题，并对启动、标签、AdGuard、正常关闭和加密落盘进行了实际验证。当前版本适合作为个人使用和公开测试版，但它仍是 WebView2 浏览器外壳，不等同于 Chromium/Edge 的完整发行版，也不应宣称已经达到主流浏览器全部安全能力。

正式支持范围为 Windows 10/11 x64。Windows 7 无法同时获得受维护的现代 Chromium/WebView2、安全补丁和当前 AdGuard 扩展能力，因此不列为支持平台。

## 已修复问题

| 级别 | 问题 | 修复 |
|---|---|---|
| 高 | 明文 WebView2 profile 位于共享 `%TEMP%` | 移至 `%LocalAppData%\VeilBrowser`，并收紧目录 ACL |
| 高 | 关闭加密失败后仍强制退出 | 保持程序运行、恢复标签并允许用户重试，不再制造“已安全退出”的假象 |
| 高 | profile ZIP 可受路径逃逸条目影响 | 拒绝绝对路径、`..` 和目标目录外路径 |
| 高 | profile v1 缺少认证 EOF，可在块边界截断 | 仅接受带认证终止记录的 v2 容器 |
| 高 | 状态、profile、主密钥包裹复用同一密钥 | 按用途派生独立子密钥，并兼容读取旧格式后迁移 |
| 高 | WebView2 锁文件导致关闭归档失败 | 使用共享读取的流式归档、跳过易失锁文件、增加有限重试 |
| 中 | 状态并发保存会争用同一个 `.new` 文件 | 使用 `SemaphoreSlim` 串行化保存 |
| 中 | 崩溃残留工作 profile 可能被旧快照覆盖 | 检测并保留非空工作目录，优先恢复较新的可恢复数据 |
| 中 | 新窗口和地址栏可传入危险协议 | 外部新窗口仅接受安全 Web 协议；导航使用明确允许列表 |
| 中 | 网站权限默认缺少用户决策 | 摄像头、麦克风、位置、通知、剪贴板等请求进入确认流程 |
| 中 | 证书错误可能继续导航 | 默认取消证书异常导航 |
| 中 | AdGuard 控制窗来源边界过宽 | 限制同一扩展来源，关闭宿主对象、DevTools 和默认右键菜单 |
| 中 | 主密码强度较低 | 新密码至少 12 字符，且必须同时含字母和数字；Argon2id 增至 4 次迭代 |
| 中 | DPAPI 和派生过程中的临时 key 未全部清零 | 对可控字节数组执行 `CryptographicOperations.ZeroMemory` |
| 中 | 收藏、密码等数据中心操作仅在退出时保存 | 新增、删除和清空立即加密保存；失败时回滚 UI 状态 |
| 低 | 下载对象和事件长期保留 | 完成/中断时解绑事件并移出字典，记录中断原因 |

## 浏览器能力改进

- 多标签工作区：网页新窗口和 `Ctrl+N` 均在当前主界面新建标签。
- 标签操作：复制标签、关闭其他标签、关闭右侧标签、恢复关闭标签。
- 快捷键：`Ctrl+T/W/N/L/F/R/D/H/J/P`、`Ctrl+Shift+T/O/Delete`、`Ctrl+1..9`、`Ctrl+Tab`、缩放、`F5/F11/F12`。
- 收藏：新增、编辑、取消收藏、资料中心打开/复制/删除，并立即持久化。
- 下载：进度、完成、取消和其他中断状态；记录可打开文件或原始网址。
- 隐私：跟踪防护可对现有标签即时更新；Cookie/WebRTC 参数明确在重启后生效。
- AdGuard：状态入口、站点开关、元素选择、自定义规则、过滤日志和完整设置。
- 视频全屏：网页全屏事件进入无边框全屏，退出后恢复窗口状态。

## 加密设计

- 浏览器状态：AES-GCM envelope。
- WebView2 profile：分块 AES-GCM v2，每块带认证标签并含认证 EOF。
- 主密码 KDF：Argon2id，64 MiB、4 次迭代、最多 4 路并行。
- 主密钥：32 字节 CSPRNG 生成；主密码模式下由派生包装密钥加密。
- Windows 账户模式：DPAPI `CurrentUser` 保护主密钥。
- 用途隔离：浏览器状态、profile 容器、主密钥包裹分别使用独立派生子密钥。
- 原子性：状态、元数据和 profile 先写临时文件，再替换正式文件。

## 验证结果

```powershell
dotnet build VeilBrowser.slnx -c Release --no-restore
dotnet run --project tests\VeilBrowser.Core.SmokeTests\VeilBrowser.Core.SmokeTests.csproj -c Release --no-build --no-restore
dotnet run --project tests\VeilBrowser.Ui.SmokeTests\VeilBrowser.Ui.SmokeTests.csproj -c Release --no-build --no-restore
dotnet list VeilBrowser.slnx package --vulnerable --include-transitive
```

结果：Release 构建 0 警告、0 错误；Core/UI 测试全部通过；直接和传递 NuGet 依赖未发现已知漏洞。

实际 GUI 验证：Release 可启动；首页、标签、快捷键和 AdGuard 面板正常；正常关闭后 `working-profile` 被删除，`profile.veil` 与 `browser-state.veil` 成功更新。

## 剩余风险与限制

1. 浏览器解锁并运行期间，WebView2 必须使用明文工作 profile。ACL 可阻止其他普通账户，但无法抵御同一用户权限下的恶意进程、管理员或内核级攻击者。
2. 密码保存在托管 `string` 中，运行期间无法保证所有副本及时清零；复制密码到系统剪贴板也会产生短期暴露。
3. 本项目继承 Evergreen WebView2 Runtime 的安全边界、媒体 DRM/编解码能力和更新状态。某些视频网站仍可能因 Widevine、平台 DRM、地区策略或站点兼容性无法播放。
4. 当前没有 Chrome Web Store 通用扩展安装、账号同步、独立进程沙箱策略管理、企业策略、Safe Browsing 下载信誉、崩溃上报和跨设备同步。
5. 冒烟测试覆盖加密格式和主要 UI 资源，但尚未建立浏览器级端到端测试矩阵；发布前仍需人工测试主流视频、登录、支付、摄像头/麦克风和多显示器 DPI。
6. 本轮没有第三方独立渗透测试或密码学审计，因此不得把本报告表述为形式化安全认证。

## 后续优先级

1. 增加自动清理密码剪贴板、下载危险文件提示和下载信誉接口。
2. 建立 Playwright/WebView2 端到端站点矩阵，覆盖视频全屏、弹窗、下载、权限和会话恢复。
3. 增加书签导入导出、下载管理器操作、页面静音、标签固定/分组和崩溃恢复提示。
4. 为发布包生成 SBOM、可复现构建清单，并在 CI 中执行依赖漏洞检查和签名验证。
