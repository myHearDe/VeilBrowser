# VeilBrowser 三主题界面

## 使用

打开“安全与隐私设置 → 外观风格”，选择主题并保存。主题值保存在加密的
`BrowserState` 中，重新启动后继续使用。

| 主题 | 设置值 | 标签布局 | 设计目标 |
|---|---|---|---|
| 午夜翡翠 | `MidnightEmerald` | 横向 | 高对比隐私品牌感，适合日常与夜间使用 |
| 瓷白日光 | `PorcelainDaylight` | 横向 | 明亮、克制、阅读友好 |
| 石墨专注 | `GraphiteFocus` | 垂直 | 为大量标签页和宽屏工作流优化 |

## 覆盖范围

- 主窗口背景、浏览器 Chrome、地址栏、标签、按钮、状态栏；
- 设置、资料中心、密码和书签编辑弹窗；
- 内置新标签页；
- AdGuard 当前网站控制中心；
- 关闭并加密时的状态遮罩。

网页本身不会被强制改色，避免破坏网站设计和视频播放。

## 实现

- `src/VeilBrowser/Infrastructure/ThemeManager.cs`：三套调色板和运行时切换；
- `src/VeilBrowser.Core/Models/BrowserTheme.cs`：持久化主题枚举；
- `src/VeilBrowser/Views/BrowserWindow.xaml.cs`：横向/垂直标签布局；
- `src/VeilBrowser/Assets/NewTab/`：三主题新标签页；
- `third_party/VeilBrowserAdGuardBridge/pages/`：三主题 AdGuard 控制中心。

主题颜色通过 WPF `DynamicResource` 引用。切换时替换应用级
`SolidColorBrush` 资源，因此已经打开的 WPF 窗口也会即时刷新，无需重启，
并且不会触发 WPF 冻结资源的只读异常。

## 预览

实现后的新标签页与 AdGuard 控制中心截图位于：

- `docs/ui-themes-preview/newtab-midnight.png`
- `docs/ui-themes-preview/newtab-daylight.png`
- `docs/ui-themes-preview/newtab-graphite.png`
- `docs/ui-themes-preview/adguard-midnight.png`
- `docs/ui-themes-preview/adguard-daylight.png`
- `docs/ui-themes-preview/adguard-graphite.png`
