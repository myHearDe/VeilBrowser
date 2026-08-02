using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Web.WebView2.Core;
using VeilBrowser.Browser;
using VeilBrowser.Core.Models;
using VeilBrowser.Core.Security;
using VeilBrowser.Infrastructure;

namespace VeilBrowser.Views;

public partial class BrowserWindow : Window
{
    private const int WmGetMinMaxInfo = 0x0024;

    private readonly AppSession _session;
    private readonly CoreWebView2Environment _webViewEnvironment;
    private readonly BrowserDownloadHandler _downloadHandler;
    private readonly BrowserExtensionManager _extensionManager;
    private readonly Dictionary<TabItem, BrowserTab> _tabs = [];
    private readonly List<string> _closedTabs = [];
    private readonly System.Windows.Threading.DispatcherTimer _autoLockTimer;
    private bool _shutdownStarted;
    private bool _allowClose;
    private bool _emergencyExit;
    private bool _clearSiteDataOnExit;
    private List<string>? _shutdownSessionUrls;
    private bool _isFullscreen;
    private bool _previousTopmost;
    private Rect _previousWindowBounds;
    private ResizeMode _previousResizeMode;
    private WindowStyle _previousWindowStyle;
    private WindowState _previousWindowState;
    private HwndSource? _windowSource;
    private string? _uninstallerToLaunch;
    private ExtensionPageWindow? _extensionWindow;

    public BrowserWindow(
        AppSession session,
        CoreWebView2Environment webViewEnvironment)
    {
        InitializeComponent();
        TopChrome.AddHandler(
            Control.MouseDoubleClickEvent,
            new MouseButtonEventHandler(TopChrome_MouseDoubleClick),
            handledEventsToo: true);
        _session = session;
        ThemeManager.Apply(_session.State.Preferences.Theme);
        ApplyThemeLayout();
        _webViewEnvironment = webViewEnvironment;
        _downloadHandler = new BrowserDownloadHandler(OnDownloadUpdated);
        _extensionManager = new BrowserExtensionManager(
            _session.Paths.AdGuardExtension,
            _session.Paths.AdGuardInstallMarker);
        _extensionManager.StateChanged += (_, _) =>
            Dispatcher.InvokeAsync(UpdateAdGuardUi);
        _autoLockTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(20)
        };
        _autoLockTimer.Tick += AutoLockTimer_Tick;
        _autoLockTimer.Start();
        if (!_session.HasMasterPassword)
        {
            LockButton.ToolTip = "加密并退出（未设置主密码，下次由 Windows 账户解锁）";
        }

        IEnumerable<string> startupUrls =
            !_session.State.Preferences.IsLocked(LockArea.Sessions) &&
            _session.State.LastSessionUrls.Count > 0
                ? _session.State.LastSessionUrls.Take(12)
                : [_session.State.Preferences.HomePage];
        foreach (var url in startupUrls)
        {
            AddTab(url);
        }
    }

    private BrowserTab? CurrentTab =>
        Tabs.SelectedItem is TabItem item && _tabs.TryGetValue(item, out var tab)
            ? tab
            : null;

    private async void AddTab(string? address = null)
    {
        var target = address is null
            ? GetNewTabAddress()
            : NormalizeAddress(address);
        var browserTab = new BrowserTab(
            _webViewEnvironment,
            _downloadHandler,
            _extensionManager,
            _session.State.Preferences.TrackingProtectionEnabled,
            url => Dispatcher.Invoke(() => AddTab(url)),
            fullscreen => Dispatcher.Invoke(() => SetFullscreen(fullscreen)),
            RequestSitePermission);

        var closeButton = new Button
        {
            Content = "×",
            Tag = browserTab
        };
        closeButton.Style = (Style)FindResource("TabCloseButton");
        closeButton.Click += CloseTab_Click;
        var title = new TextBlock
        {
            Text = "新标签页",
            Width = 170,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(title);
        header.Children.Add(closeButton);
        var item = new TabItem
        {
            Header = header,
            Tag = title,
            Style = GetTabItemStyle()
        };
        WindowChrome.SetIsHitTestVisibleInChrome(item, true);
        item.ContextMenu = CreateTabContextMenu(item, browserTab);

        browserTab.TitleChanged += (_, newTitle) =>
        {
            title.Text = newTitle;
            if (ReferenceEquals(CurrentTab, browserTab))
            {
                Title = $"{newTitle} — 隐栈浏览器";
            }
        };
        browserTab.AddressChanged += (_, newAddress) =>
        {
            if (ReferenceEquals(CurrentTab, browserTab))
            {
                AddressBox.Text = newAddress;
                UpdateBookmarkUi();
            }
        };
        browserTab.LoadingStateChanged += (_, isLoading) =>
        {
            if (ReferenceEquals(CurrentTab, browserTab))
            {
                StatusText.Text = isLoading ? "正在加载…" : "就绪";
            }
            if (!isLoading)
            {
                RecordHistory(browserTab);
            }
        };
        browserTab.StatusMessageChanged += (_, value) =>
        {
            if (ReferenceEquals(CurrentTab, browserTab))
            {
                StatusText.Text = string.IsNullOrWhiteSpace(value) ? "就绪" : value;
            }
        };

        _tabs[item] = browserTab;
        Tabs.Items.Add(item);
        Tabs.SelectedItem = item;
        ShowTab(browserTab);
        try
        {
            await browserTab.InitializeAsync(target);
        }
        catch (Exception ex)
        {
            title.Text = "页面初始化失败";
            StatusText.Text = $"页面初始化失败：{ex.Message}";
        }
    }

    private void UpdateAdGuardUi()
    {
        if (_extensionManager.IsReady)
        {
            AdGuardStatusDot.Fill = _extensionManager.IsEnabled
                ? (System.Windows.Media.Brush)FindResource("ProtectBrush")
                : (System.Windows.Media.Brush)FindResource("MutedTextBrush");
            AdGuardBadge.Text = _extensionManager.IsEnabled ? "ON" : "OFF";
            AdGuardButton.ToolTip = _extensionManager.IsEnabled
                ? $"AdGuard 正在保护网页 · Chromium {_extensionManager.RuntimeVersion}"
                : $"AdGuard 已暂停 · Chromium {_extensionManager.RuntimeVersion}";
        }
        else
        {
            AdGuardStatusDot.Fill = (System.Windows.Media.Brush)FindResource("DangerBrush");
            AdGuardBadge.Text = "!";
            AdGuardButton.ToolTip = _extensionManager.ErrorMessage is { Length: > 0 } error
                ? $"AdGuard 加载失败：{error}"
                : "正在加载 AdGuard…";
        }
    }

    private void AdGuard_Click(object sender, RoutedEventArgs e)
    {
        var controlUrl = _extensionManager.GetPageUrl("veil-control.html");
        if (controlUrl is null)
        {
            MessageBox.Show(
                _extensionManager.ErrorMessage ?? "AdGuard 尚未完成初始化，请稍后重试。",
                "AdGuard",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        controlUrl += "?theme=" + GetThemeQueryValue(_session.State.Preferences.Theme);
        var targetAddress = CurrentTab?.Address;
        if (!string.IsNullOrWhiteSpace(targetAddress))
        {
            controlUrl += "&target=" + Uri.EscapeDataString(targetAddress);
        }

        _extensionWindow?.Close();
        _extensionWindow = new ExtensionPageWindow(
            _webViewEnvironment,
            controlUrl,
            url => AddTab(url))
        {
            Owner = this,
            Left = Math.Max(0, Left + ActualWidth - 438),
            Top = Math.Max(0, Top + 96)
        };
        _extensionWindow.Closed += (_, _) => _extensionWindow = null;
        _extensionWindow.Show();
    }

    private void AdGuardMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        var siteControl = new MenuItem
        {
            Header = "当前网站控制与手动拦截",
            IsEnabled = _extensionManager.IsReady
        };
        siteControl.Click += (_, _) => AdGuard_Click(this, new RoutedEventArgs());
        var toggle = new MenuItem
        {
            Header = _extensionManager.IsEnabled
                ? "暂停 AdGuard 防护"
                : "启用 AdGuard 防护",
            IsEnabled = _extensionManager.IsReady
        };
        toggle.Click += async (_, _) =>
        {
            try
            {
                await _extensionManager.SetEnabledAsync(!_extensionManager.IsEnabled);
                CurrentTab?.Reload();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"无法切换 AdGuard：{ex.Message}",
                    "AdGuard",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };

        var userRules = new MenuItem { Header = "自定义过滤规则" };
        userRules.Click += (_, _) => OpenAdGuardPage("fullscreen-user-rules.html");
        var filteringLog = new MenuItem { Header = "过滤日志" };
        filteringLog.Click += (_, _) => OpenAdGuardPage("filtering-log.html");
        var settings = new MenuItem { Header = "AdGuard 完整设置" };
        settings.Click += (_, _) => OpenAdGuardPage("options.html");

        menu.Items.Add(siteControl);
        menu.Items.Add(toggle);
        menu.Items.Add(new Separator());
        menu.Items.Add(userRules);
        menu.Items.Add(filteringLog);
        menu.Items.Add(settings);
        if (sender is Button placementTarget)
        {
            menu.PlacementTarget = placementTarget;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        }
        menu.IsOpen = true;
    }

    private void OpenAdGuardPage(string page)
    {
        var url = _extensionManager.GetPageUrl(page);
        if (url is null)
        {
            MessageBox.Show(
                _extensionManager.ErrorMessage ?? "AdGuard 尚未完成初始化，请稍后重试。",
                "AdGuard",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        AddTab(url);
    }

    private ContextMenu CreateTabContextMenu(TabItem item, BrowserTab tab)
    {
        var menu = new ContextMenu();
        var duplicate = new MenuItem { Header = "复制标签页" };
        duplicate.Click += (_, _) => AddTab(tab.Address);
        var reopen = new MenuItem { Header = "重新打开关闭的标签页 (Ctrl+Shift+T)" };
        reopen.Click += (_, _) => ReopenClosedTab();
        var close = new MenuItem { Header = "关闭标签页" };
        close.Click += (_, _) => CloseTab(tab);
        var closeOthers = new MenuItem { Header = "关闭其他标签页" };
        closeOthers.Click += (_, _) =>
        {
            foreach (var other in _tabs.Values.Where(x => !ReferenceEquals(x, tab)).ToList())
            {
                CloseTab(other, remember: false);
            }
        };
        var closeRight = new MenuItem { Header = "关闭右侧标签页" };
        closeRight.Click += (_, _) =>
        {
            var index = Tabs.Items.IndexOf(item);
            var toClose = Tabs.Items.Cast<TabItem>()
                .Skip(index + 1)
                .Select(x => _tabs[x])
                .ToList();
            foreach (var other in toClose)
            {
                CloseTab(other, remember: false);
            }
        };
        menu.Items.Add(duplicate);
        menu.Items.Add(reopen);
        menu.Items.Add(new Separator());
        menu.Items.Add(close);
        menu.Items.Add(closeOthers);
        menu.Items.Add(closeRight);
        return menu;
    }

    private void ShowTab(BrowserTab tab)
    {
        BrowserHost.Children.Clear();
        BrowserHost.Children.Add(tab.Browser);
        AddressBox.Text = tab.Address;
        Title = $"{tab.Title} — 隐栈浏览器";
        UpdateBookmarkUi();
        tab.Browser.Focus();
    }

    private void RecordHistory(BrowserTab tab)
    {
        var url = tab.Address;
        if (string.IsNullOrWhiteSpace(url) ||
            !(url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
              url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var recent = _session.State.History.LastOrDefault();
        if (recent?.Url == url &&
            DateTimeOffset.Now - recent.VisitedAt < TimeSpan.FromSeconds(30))
        {
            return;
        }

        _session.State.History.Add(new HistoryEntry(tab.Title, url, DateTimeOffset.Now));
        if (_session.State.History.Count > 10_000)
        {
            _session.State.History.RemoveRange(0, _session.State.History.Count - 10_000);
        }
    }

    private void OnDownloadUpdated(DownloadEntry entry)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_session.State.Downloads.All(x => x.Id != entry.Id))
            {
                _session.State.Downloads.Add(entry);
            }

            StatusText.Text = entry.IsComplete
                ? $"下载完成：{entry.FileName}"
                : entry.IsCancelled
                    ? $"下载已取消：{entry.FileName}"
                    : entry.IsInterrupted
                        ? $"下载中断：{entry.FileName} ({entry.InterruptReason})"
                    : $"正在下载：{entry.FileName}";
        });
    }

    private bool EnsureAreaAccess(LockArea area, string areaName)
    {
        if (!_session.State.Preferences.IsLocked(area))
        {
            return true;
        }

        var prompt = new PasswordPromptWindow(_session, areaName) { Owner = this };
        return prompt.ShowDialog() == true;
    }

    private void NewTab_Click(object sender, RoutedEventArgs e) => AddTab();

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: BrowserTab tab })
        {
            CloseTab(tab);
        }
    }

    private void CloseTab(BrowserTab tab, bool remember = true)
    {
        var item = _tabs.FirstOrDefault(x => ReferenceEquals(x.Value, tab)).Key;
        if (item is null)
        {
            return;
        }

        if (remember && !string.IsNullOrWhiteSpace(tab.Address))
        {
            _closedTabs.Add(tab.Address);
            if (_closedTabs.Count > 20)
            {
                _closedTabs.RemoveAt(0);
            }
        }

        if (ReferenceEquals(CurrentTab, tab))
        {
            BrowserHost.Children.Clear();
        }
        _tabs.Remove(item);
        Tabs.Items.Remove(item);
        tab.Dispose();
        if (_tabs.Count == 0 && !_shutdownStarted)
        {
            AddTab();
        }
    }

    private void ReopenClosedTab()
    {
        if (_closedTabs.Count == 0)
        {
            StatusText.Text = "没有可重新打开的标签页";
            return;
        }

        var index = _closedTabs.Count - 1;
        var address = _closedTabs[index];
        _closedTabs.RemoveAt(index);
        AddTab(address);
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CurrentTab is { } tab)
        {
            ShowTab(tab);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => CurrentTab?.Back();

    private void Forward_Click(object sender, RoutedEventArgs e) => CurrentTab?.Forward();

    private void Reload_Click(object sender, RoutedEventArgs e) => CurrentTab?.Reload();

    private void Home_Click(object sender, RoutedEventArgs e) =>
        CurrentTab?.Navigate(_session.State.Preferences.HomePage);

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || CurrentTab is null)
        {
            return;
        }

        e.Handled = true;
        CurrentTab.Navigate(NormalizeAddress(AddressBox.Text));
        Keyboard.ClearFocus();
    }

    private void AddressBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        AddressBox.SelectAll();

    private async void Bookmark_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentTab is null || string.IsNullOrWhiteSpace(CurrentTab.Address))
        {
            StatusText.Text = "当前页面无法收藏";
            return;
        }

        var url = CurrentTab.Address;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            StatusText.Text = "只有 http/https 页面可以加入收藏";
            return;
        }

        var existing = FindBookmark(url);
        var title = string.IsNullOrWhiteSpace(CurrentTab.Title)
            ? uri.Host
            : CurrentTab.Title;
        var editor = new BookmarkEditorWindow(title, url, existing)
        {
            Owner = this
        };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        var previousBookmarks = _session.State.Bookmarks.ToList();
        string successMessage;
        if (editor.DeleteRequested && existing is not null)
        {
            _session.State.Bookmarks.Remove(existing);
            successMessage = "已取消收藏";
        }
        else if (editor.Entry is not null)
        {
            if (existing is null)
            {
                _session.State.Bookmarks.Add(editor.Entry);
                successMessage = "已保存到收藏夹";
            }
            else
            {
                var index = _session.State.Bookmarks.IndexOf(existing);
                if (index >= 0)
                {
                    _session.State.Bookmarks[index] = editor.Entry;
                }
                successMessage = "收藏已更新";
            }
        }
        else
        {
            return;
        }

        try
        {
            // Favorites are user data, so persist immediately rather than
            // waiting for the browser to close successfully.
            await _session.SaveAsync();
            StatusText.Text = successMessage;
            UpdateBookmarkUi();
        }
        catch (Exception ex)
        {
            _session.State.Bookmarks = previousBookmarks;
            UpdateBookmarkUi();
            MessageBox.Show(
                $"无法保存收藏：{ex.Message}",
                "收藏失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private BookmarkEntry? FindBookmark(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return _session.State.Bookmarks.FirstOrDefault(
            bookmark => string.Equals(
                bookmark.Url.TrimEnd('/'),
                url.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateBookmarkUi()
    {
        var bookmarked = FindBookmark(CurrentTab?.Address) is not null;
        BookmarkGlyph.Text = bookmarked ? "\uE735" : "\uE734";
        BookmarkButton.ToolTip = bookmarked
            ? "编辑或取消收藏 (Ctrl+D)"
            : "收藏当前页 (Ctrl+D)";
        BookmarkButton.SetResourceReference(
            ForegroundProperty,
            bookmarked ? "AccentBrush" : "TextBrush");
    }

    private void DataCenter_Click(object sender, RoutedEventArgs e) =>
        OpenDataCenter(LockArea.History);

    private void OpenDataCenter(LockArea initialArea)
    {
        var window = new DataCenterWindow(
            _session,
            EnsureAreaAccess,
            () => _clearSiteDataOnExit = true,
            url => AddTab(url),
            initialArea)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureAreaAccess(LockArea.Settings, "安全设置"))
        {
            return;
        }

        var previousTrackingProtection =
            _session.State.Preferences.TrackingProtectionEnabled;
        var previousThirdPartyCookies =
            _session.State.Preferences.BlockThirdPartyCookies;
        var previousWebRtcProtection =
            _session.State.Preferences.WebRtcLeakProtection;
        var window = new SecuritySettingsWindow(_session) { Owner = this };
        if (window.ShowDialog() == true)
        {
            ThemeManager.Apply(_session.State.Preferences.Theme);
            ApplyThemeLayout();
            if (previousTrackingProtection !=
                _session.State.Preferences.TrackingProtectionEnabled)
            {
                foreach (var tab in _tabs.Values)
                {
                    tab.SetTrackingProtection(
                        _session.State.Preferences.TrackingProtectionEnabled);
                }
            }

            if (previousThirdPartyCookies !=
                    _session.State.Preferences.BlockThirdPartyCookies ||
                previousWebRtcProtection !=
                    _session.State.Preferences.WebRtcLeakProtection)
            {
                StatusText.Text = "Cookie 或 WebRTC 防护将在重启浏览器后完全生效";
            }
            if (_session.HasMasterPassword)
            {
                LockButton.ToolTip = "立即锁定并加密退出";
            }
        }
    }

    private async void Lock_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_session.HasMasterPassword)
            {
                await _session.Security.ForcePasswordPromptOnNextLaunchAsync();
            }
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"无法锁定浏览器：{ex.Message}",
                "锁定失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        var newTab = new MenuItem { Header = "新建标签页 (Ctrl+T)" };
        newTab.Click += (_, _) => AddTab();
        var reopen = new MenuItem { Header = "重新打开关闭的标签页 (Ctrl+Shift+T)" };
        reopen.Click += (_, _) => ReopenClosedTab();
        var history = new MenuItem { Header = "历史记录 (Ctrl+H)" };
        history.Click += (_, _) => OpenDataCenter(LockArea.History);
        var bookmarks = new MenuItem { Header = "书签收藏 (Ctrl+Shift+O)" };
        bookmarks.Click += (_, _) => OpenDataCenter(LockArea.Bookmarks);
        var downloads = new MenuItem { Header = "下载记录 (Ctrl+J)" };
        downloads.Click += (_, _) => OpenDataCenter(LockArea.Downloads);
        var print = new MenuItem { Header = "打印… (Ctrl+P)" };
        print.Click += (_, _) => CurrentTab?.Print();
        var find = new MenuItem { Header = "在页面中查找… (Ctrl+F)" };
        find.Click += (_, _) => ShowFind();
        var devTools = new MenuItem { Header = "开发者工具 (F12)" };
        devTools.Click += (_, _) => CurrentTab?.ShowDevTools();
        var zoomIn = new MenuItem { Header = "放大" };
        zoomIn.Click += (_, _) => CurrentTab?.ZoomIn();
        var zoomOut = new MenuItem { Header = "缩小" };
        zoomOut.Click += (_, _) => CurrentTab?.ZoomOut();
        var zoomReset = new MenuItem { Header = "重置缩放" };
        zoomReset.Click += (_, _) => CurrentTab?.ResetZoom();
        var viewSource = new MenuItem { Header = "查看网页源代码" };
        viewSource.Click += (_, _) => CurrentTab?.ViewSource();
        var fullscreen = new MenuItem { Header = "全屏 (F11)" };
        fullscreen.Click += (_, _) => SetFullscreen(!_isFullscreen);
        var emergency = new MenuItem { Header = "紧急退出并清除全部浏览数据" };
        emergency.Click += (_, _) =>
        {
            if (MessageBox.Show(
                    "这会删除历史、书签、密码库、Cookie、缓存和会话，且无法撤销。是否继续？",
                    "紧急退出",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                _emergencyExit = true;
                _session.State.History.Clear();
                _session.State.Bookmarks.Clear();
                _session.State.Downloads.Clear();
                _session.State.Credentials.Clear();
                _session.State.LastSessionUrls.Clear();
                Close();
            }
        };
        var uninstall = new MenuItem { Header = "卸载或清理本机数据…" };
        uninstall.Click += (_, _) => StartUninstall();

        menu.Items.Add(newTab);
        menu.Items.Add(reopen);
        menu.Items.Add(new Separator());
        menu.Items.Add(history);
        menu.Items.Add(bookmarks);
        menu.Items.Add(downloads);
        menu.Items.Add(new Separator());
        menu.Items.Add(print);
        menu.Items.Add(find);
        menu.Items.Add(zoomIn);
        menu.Items.Add(zoomOut);
        menu.Items.Add(zoomReset);
        menu.Items.Add(fullscreen);
        menu.Items.Add(viewSource);
        menu.Items.Add(devTools);
        menu.Items.Add(new Separator());
        menu.Items.Add(emergency);
        menu.Items.Add(uninstall);
        if (sender is Button placementTarget)
        {
            menu.PlacementTarget = placementTarget;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        }
        menu.IsOpen = true;
    }

    private void StartUninstall()
    {
        var uninstaller = Path.Combine(AppContext.BaseDirectory, "unins000.exe");
        if (!File.Exists(uninstaller))
        {
            var cleanupScript = Path.Combine(AppContext.BaseDirectory, "Clean-Local-Data.ps1");
            MessageBox.Show(
                File.Exists(cleanupScript)
                    ? "当前运行的是免安装版，没有系统卸载项。\n\n" +
                      "删除程序文件夹即可移除程序；如需同时清除浏览资料，请先正常关闭浏览器，" +
                      "再运行程序目录中的 Clean-Local-Data.ps1。"
                    : "当前运行的是开发版或免安装版，没有系统卸载项。\n\n" +
                      "请先正常关闭浏览器，再删除程序目录。浏览资料位于 " +
                      "%LocalAppData%\\VeilBrowser。",
                "卸载与清理",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(
                "浏览器将先正常保存、加密并关闭，然后打开卸载程序。\n\n" +
                "卸载时可以选择“保留浏览资料”或“彻底删除全部本地数据”。是否继续？",
                "卸载隐栈浏览器",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _uninstallerToLaunch = uninstaller;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.T)
        {
            ReopenClosedTab();
            e.Handled = true;
        }
        else if (modifiers == ModifierKeys.Control && e.Key == Key.T)
        {
            AddTab();
            e.Handled = true;
        }
        else if (modifiers == ModifierKeys.Control && e.Key == Key.N)
        {
            AddTab();
            e.Handled = true;
        }
        else if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) &&
                 e.Key == Key.Delete)
        {
            OpenDataCenter(LockArea.CookiesAndSiteData);
            e.Handled = true;
        }
        else if (modifiers == ModifierKeys.Control &&
                 TryGetTabNumber(e.Key, out var tabNumber))
        {
            SelectTabByNumber(tabNumber);
            e.Handled = true;
        }
        else if (modifiers == ModifierKeys.Control && e.Key == Key.W)
        {
            if (CurrentTab is { } tab)
            {
                CloseTab(tab);
            }
            e.Handled = true;
        }
        else if (modifiers == ModifierKeys.Control && e.Key == Key.L)
        {
            AddressBox.Focus();
            AddressBox.SelectAll();
            e.Handled = true;
        }
        else if (modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            ShowFind();
            e.Handled = true;
        }
        else if (modifiers == ModifierKeys.Control && e.Key is Key.R)
        {
            CurrentTab?.Reload();
            e.Handled = true;
        }
        else if (modifiers == ModifierKeys.Control && e.Key == Key.D)
        {
            Bookmark_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) &&
                 e.Key == Key.O)
        {
            OpenDataCenter(LockArea.Bookmarks);
            e.Handled = true;
        }
        else if (modifiers == ModifierKeys.Control && e.Key == Key.H)
        {
            OpenDataCenter(LockArea.History);
            e.Handled = true;
        }
        else if (modifiers == ModifierKeys.Control && e.Key == Key.J)
        {
            OpenDataCenter(LockArea.Downloads);
            e.Handled = true;
        }
        else if (modifiers == ModifierKeys.Control && e.Key == Key.P)
        {
            CurrentTab?.Print();
            e.Handled = true;
        }
        else if (modifiers == ModifierKeys.Control &&
                 (e.Key == Key.Add || e.Key == Key.OemPlus))
        {
            CurrentTab?.ZoomIn();
            e.Handled = true;
        }
        else if (modifiers == ModifierKeys.Control &&
                 (e.Key == Key.Subtract || e.Key == Key.OemMinus))
        {
            CurrentTab?.ZoomOut();
            e.Handled = true;
        }
        else if (modifiers == ModifierKeys.Control &&
                 (e.Key == Key.D0 || e.Key == Key.NumPad0))
        {
            CurrentTab?.ResetZoom();
            e.Handled = true;
        }
        else if ((modifiers & ModifierKeys.Control) != 0 && e.Key == Key.Tab)
        {
            SelectAdjacentTab((modifiers & ModifierKeys.Shift) != 0 ? -1 : 1);
            e.Handled = true;
        }
        else if (modifiers == ModifierKeys.Alt && e.SystemKey == Key.Left)
        {
            CurrentTab?.Back();
            e.Handled = true;
        }
        else if (modifiers == ModifierKeys.Alt && e.SystemKey == Key.Right)
        {
            CurrentTab?.Forward();
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            CurrentTab?.Reload();
            e.Handled = true;
        }
        else if (e.Key == Key.F11)
        {
            SetFullscreen(!_isFullscreen);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _isFullscreen)
        {
            SetFullscreen(false);
            e.Handled = true;
        }
        else if (e.Key == Key.F12)
        {
            CurrentTab?.ShowDevTools();
            e.Handled = true;
        }
    }

    private void SelectAdjacentTab(int offset)
    {
        if (Tabs.Items.Count < 2)
        {
            return;
        }

        var currentIndex = Math.Max(0, Tabs.SelectedIndex);
        Tabs.SelectedIndex = (currentIndex + offset + Tabs.Items.Count) % Tabs.Items.Count;
    }

    private void SelectTabByNumber(int tabNumber)
    {
        if (Tabs.Items.Count == 0)
        {
            return;
        }

        var index = tabNumber == 9
            ? Tabs.Items.Count - 1
            : Math.Min(tabNumber - 1, Tabs.Items.Count - 1);
        Tabs.SelectedIndex = index;
    }

    private static bool TryGetTabNumber(Key key, out int number)
    {
        number = key switch
        {
            >= Key.D1 and <= Key.D9 => (int)key - (int)Key.D0,
            >= Key.NumPad1 and <= Key.NumPad9 => (int)key - (int)Key.NumPad0,
            _ => 0
        };
        return number != 0;
    }

    private async void ShowFind()
    {
        var query = Microsoft.VisualBasic.Interaction.InputBox(
            "输入要查找的文字：",
            "在页面中查找");
        if (!string.IsNullOrWhiteSpace(query) && CurrentTab is { } tab)
        {
            await tab.FindAsync(query);
        }
    }

    private void SetFullscreen(bool fullscreen)
    {
        if (_isFullscreen == fullscreen)
        {
            return;
        }

        _isFullscreen = fullscreen;
        TopChrome.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
        NavigationChrome.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
        StatusChrome.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
        WindowControlPanel.Visibility =
            fullscreen ? Visibility.Collapsed : Visibility.Visible;

        if (fullscreen)
        {
            BorderThickness = new Thickness(0);
            TopTabsRow.Height = new GridLength(0);
            NavigationRow.Height = new GridLength(0);
            StatusRow.Height = new GridLength(0);
            SidebarColumn.Width = new GridLength(0);
            Grid.SetColumn(BrowserSurface, 0);
            Grid.SetColumnSpan(BrowserSurface, 2);
            Grid.SetRow(BrowserSurface, 2);
            Grid.SetRowSpan(BrowserSurface, 1);
            _previousWindowStyle = WindowStyle;
            _previousWindowState = WindowState;
            _previousTopmost = Topmost;
            _previousResizeMode = ResizeMode;
            _previousWindowBounds = RestoreBounds;

            // A WebView video fullscreen request must use the complete monitor,
            // not the taskbar work area used by ordinary maximized windows.
            // Use a normal borderless window at the exact monitor rectangle:
            // Windows otherwise keeps the taskbar above maximized WPF windows.
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            Activate();
            WindowWorkAreaHelper.ApplyFullscreenBounds(
                new WindowInteropHelper(this).Handle);
        }
        else
        {
            BorderThickness = new Thickness(1);
            ApplyThemeLayout();
            WindowState = WindowState.Normal;
            Topmost = _previousTopmost;
            WindowStyle = _previousWindowStyle;
            ResizeMode = _previousResizeMode;
            Left = _previousWindowBounds.Left;
            Top = _previousWindowBounds.Top;
            Width = _previousWindowBounds.Width;
            Height = _previousWindowBounds.Height;
            WindowState = _previousWindowState;
        }
    }

    private void ApplyThemeLayout()
    {
        var vertical = _session.State.Preferences.Theme == BrowserTheme.GraphiteFocus;
        SidebarColumn.Width = vertical
            ? new GridLength(230)
            : new GridLength(0);
        ContentColumn.Width = new GridLength(1, GridUnitType.Star);

        if (vertical)
        {
            TopTabsRow.Height = new GridLength(56);
            NavigationRow.Height = new GridLength(0);
            StatusRow.Height = new GridLength(26);

            Grid.SetColumn(TopChrome, 0);
            Grid.SetColumnSpan(TopChrome, 1);
            Grid.SetRow(TopChrome, 0);
            Grid.SetRowSpan(TopChrome, 4);
            TopChrome.BorderThickness = new Thickness(0, 0, 1, 0);

            BrandColumn.Width = new GridLength(1, GridUnitType.Star);
            TabsColumn.Width = new GridLength(0);
            NewTabColumn.Width = new GridLength(0);
            WindowControlsSpacerColumn.Width = new GridLength(0);
            BrandRow.Height = new GridLength(58);
            TabsRow.Height = new GridLength(1, GridUnitType.Star);
            NewTabRow.Height = new GridLength(54);

            Grid.SetColumn(BrandPanel, 0);
            Grid.SetRow(BrandPanel, 0);
            BrandPanel.Margin = new Thickness(15, 0, 8, 0);
            Grid.SetColumn(Tabs, 0);
            Grid.SetRow(Tabs, 1);
            Tabs.TabStripPlacement = Dock.Left;
            Tabs.Template = (ControlTemplate)FindResource("VerticalBrowserTabsTemplate");
            Tabs.ItemContainerStyle = GetTabItemStyle();
            Grid.SetColumn(NewTabButton, 0);
            Grid.SetRow(NewTabButton, 2);
            NewTabButton.Width = double.NaN;
            NewTabButton.Height = 38;
            NewTabButton.Margin = new Thickness(10, 6, 10, 10);

            Grid.SetColumn(NavigationChrome, 1);
            Grid.SetColumnSpan(NavigationChrome, 1);
            Grid.SetRow(NavigationChrome, 0);
            Grid.SetRowSpan(NavigationChrome, 1);
            NavigationChrome.Padding = new Thickness(0, 0, 138, 0);
            Grid.SetColumn(BrowserSurface, 1);
            Grid.SetColumnSpan(BrowserSurface, 1);
            Grid.SetRow(BrowserSurface, 1);
            Grid.SetRowSpan(BrowserSurface, 2);
            Grid.SetColumn(StatusChrome, 1);
            Grid.SetColumnSpan(StatusChrome, 1);
            Grid.SetRow(StatusChrome, 3);
        }
        else
        {
            TopTabsRow.Height = new GridLength(46);
            NavigationRow.Height = new GridLength(56);
            StatusRow.Height = new GridLength(26);

            Grid.SetColumn(TopChrome, 0);
            Grid.SetColumnSpan(TopChrome, 2);
            Grid.SetRow(TopChrome, 0);
            Grid.SetRowSpan(TopChrome, 1);
            TopChrome.BorderThickness = new Thickness(0, 0, 0, 1);

            BrandColumn.Width = new GridLength(116);
            TabsColumn.Width = new GridLength(1, GridUnitType.Star);
            NewTabColumn.Width = GridLength.Auto;
            WindowControlsSpacerColumn.Width = new GridLength(138);
            BrandRow.Height = new GridLength(1, GridUnitType.Star);
            TabsRow.Height = new GridLength(0);
            NewTabRow.Height = new GridLength(0);

            Grid.SetColumn(BrandPanel, 0);
            Grid.SetRow(BrandPanel, 0);
            BrandPanel.Margin = new Thickness(15, 0, 8, 0);
            Grid.SetColumn(Tabs, 1);
            Grid.SetRow(Tabs, 0);
            Tabs.TabStripPlacement = Dock.Top;
            Tabs.Template = (ControlTemplate)FindResource("HorizontalBrowserTabsTemplate");
            Tabs.ItemContainerStyle = GetTabItemStyle();
            Grid.SetColumn(NewTabButton, 2);
            Grid.SetRow(NewTabButton, 0);
            NewTabButton.Width = 38;
            NewTabButton.Height = 38;
            NewTabButton.Margin = new Thickness(6, 3, 10, 3);

            Grid.SetColumn(NavigationChrome, 0);
            Grid.SetColumnSpan(NavigationChrome, 2);
            Grid.SetRow(NavigationChrome, 1);
            Grid.SetRowSpan(NavigationChrome, 1);
            NavigationChrome.Padding = new Thickness(0);
            Grid.SetColumn(BrowserSurface, 0);
            Grid.SetColumnSpan(BrowserSurface, 2);
            Grid.SetRow(BrowserSurface, 2);
            Grid.SetRowSpan(BrowserSurface, 1);
            Grid.SetColumn(StatusChrome, 0);
            Grid.SetColumnSpan(StatusChrome, 2);
            Grid.SetRow(StatusChrome, 3);
        }

        var tabItemStyle = GetTabItemStyle();
        foreach (var tabItem in _tabs.Keys)
        {
            // Explicit TabItem instances do not automatically inherit
            // TabControl.ItemContainerStyle, so update every open tab.
            tabItem.Style = tabItemStyle;
        }
    }

    private Style GetTabItemStyle() =>
        (Style)FindResource(
            _session.State.Preferences.Theme == BrowserTheme.GraphiteFocus
                ? "VerticalBrowserTabItemStyle"
                : "BrowserTabItemStyle");

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeRestoreWindow_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void TopChrome_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left ||
            _isFullscreen ||
            IsInsideButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        e.Handled = true;
    }

    private static bool IsInsideButton(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is Button)
            {
                return true;
            }

            element = element is Visual
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return false;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource?.AddHook(WindowMessageHook);
    }

    private nint WindowMessageHook(
        nint windowHandle,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        if (message == WmGetMinMaxInfo)
        {
            WindowWorkAreaHelper.ApplyToMinMaxInfo(
                windowHandle,
                longParameter,
                useWorkArea: !_isFullscreen);
            handled = true;
        }

        return nint.Zero;
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        var maximized = WindowState == WindowState.Maximized;
        if (!_isFullscreen)
        {
            BorderThickness = maximized
                ? new Thickness(0)
                : new Thickness(1);
        }
        MaximizeRestoreGlyph.Text = maximized ? "\uE923" : "\uE922";
        MaximizeRestoreButton.ToolTip = maximized ? "还原" : "最大化";
    }

    private static string GetThemeQueryValue(BrowserTheme theme) => theme switch
    {
        BrowserTheme.PorcelainDaylight => "daylight",
        BrowserTheme.GraphiteFocus => "graphite",
        _ => "midnight"
    };

    private string GetNewTabAddress() =>
        "https://veil.local/index.html?theme=" +
        GetThemeQueryValue(_session.State.Preferences.Theme);

    private async void AutoLockTimer_Tick(object? sender, EventArgs e)
    {
        var minutes = _session.State.Preferences.AutoLockMinutes;
        if (minutes <= 0 ||
            SystemIdleTime.GetIdleDuration() < TimeSpan.FromMinutes(minutes))
        {
            return;
        }

        _autoLockTimer.Stop();
        try
        {
            if (_session.HasMasterPassword)
            {
                await _session.Security.ForcePasswordPromptOnNextLaunchAsync();
            }
            Close();
        }
        catch (Exception ex)
        {
            _autoLockTimer.Start();
            MessageBox.Show(
                $"自动锁定失败：{ex.Message}",
                "锁定失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        _autoLockTimer.Stop();
        // The standard WPF WebView2 control is an HwndHost and otherwise renders
        // above WPF overlays. Hide it before showing the shutdown progress UI.
        BrowserHost.Visibility = Visibility.Collapsed;
        ClosingOverlay.Visibility = Visibility.Visible;
        IsEnabled = false;
        try
        {
            if (!_emergencyExit)
            {
                _shutdownSessionUrls = _tabs.Values
                    .Select(x => x.Address)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();
                _session.State.LastSessionUrls = _shutdownSessionUrls.ToList();

                if (_clearSiteDataOnExit && _tabs.Values.FirstOrDefault() is { } siteDataTab)
                {
                    ClosingText.Text = "正在清除 Cookie 与网站数据…";
                    await siteDataTab.ClearBrowsingDataAsync(
                        CoreWebView2BrowsingDataKinds.AllSite |
                        CoreWebView2BrowsingDataKinds.DiskCache |
                        CoreWebView2BrowsingDataKinds.ServiceWorkers);
                }
                else if (_session.State.Preferences.ClearCacheOnExit &&
                         _tabs.Values.FirstOrDefault() is { } cacheTab)
                {
                    await cacheTab.ClearBrowsingDataAsync(
                        CoreWebView2BrowsingDataKinds.DiskCache |
                        CoreWebView2BrowsingDataKinds.CacheStorage);
                }
            }

            var webViewProcessIds = _webViewEnvironment.GetProcessInfos()
                .Select(x => x.ProcessId)
                .ToHashSet();
            _extensionWindow?.Close();
            _extensionWindow = null;
            foreach (var tab in _tabs.Values.ToList())
            {
                tab.Dispose();
            }
            _tabs.Clear();
            BrowserHost.Children.Clear();
            await WaitForWebViewProcessesToExitAsync(webViewProcessIds);

            if (_emergencyExit)
            {
                ClosingText.Text = "正在清除浏览器资料…";
                await DeleteAllLocalDataAsync();
            }
            else
            {
                await _session.SaveAsync();
                ClosingText.Text = "正在加密浏览器资料…";
                await ProfileContainerService.ProtectAsync(
                    _session.Paths.WorkingProfile,
                    _session.Paths.EncryptedProfile,
                    _session.MasterKey);
            }
        }
        catch (Exception ex)
        {
            // Keep the process alive when persistence/encryption fails. Closing
            // here would leave the user with a false sense of secure exit and
            // could strand the only recoverable copy in the working profile.
            _shutdownStarted = false;
            BrowserHost.Visibility = Visibility.Visible;
            ClosingOverlay.Visibility = Visibility.Collapsed;
            IsEnabled = true;
            _autoLockTimer.Start();
            if (!_emergencyExit)
            {
                var reopenUrls = _shutdownSessionUrls is { Count: > 0 }
                    ? _shutdownSessionUrls.ToList()
                    : [_session.State.Preferences.HomePage];
                _shutdownSessionUrls = null;
                foreach (var url in reopenUrls.Take(12))
                {
                    AddTab(url);
                }
            }
            MessageBox.Show(
                _emergencyExit
                    ? $"未能完全清除本地数据：{ex.Message}\n\n请关闭浏览器后运行 Clean-Local-Data.ps1。"
                    : $"关闭时未能完成加密：{ex.Message}\n\n为避免丢失数据，浏览器不会自动删除工作目录。",
                _emergencyExit ? "清理未完成" : "加密失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        _allowClose = true;
        if (_uninstallerToLaunch is not null)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _uninstallerToLaunch,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"无法打开卸载程序：{ex.Message}",
                    "卸载失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        Close();
    }

    private static async Task WaitForWebViewProcessesToExitAsync(
        IReadOnlySet<int> processIds)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (processIds.All(HasExited))
            {
                return;
            }
            await Task.Delay(100);
        }

        // Profile archiving has its own retry loop for late file-handle release.
        static bool HasExited(int processId)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                return process.HasExited;
            }
            catch (ArgumentException)
            {
                return true;
            }
        }
    }

    private static string NormalizeAddress(string input)
    {
        var value = input.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https" or "view-source" or "chrome-extension")
        {
            return uri.AbsoluteUri;
        }

        if (string.Equals(value, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        if (!value.Contains(' ') && value.Contains('.'))
        {
            return "https://" + value;
        }

        var hostCandidate = value.Split('/', '\\')[0].Split(':')[0];
        if (!value.Contains(' ') &&
            (hostCandidate.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
             IPAddress.TryParse(hostCandidate, out _)))
        {
            return "https://" + value;
        }

        return "https://www.bing.com/search?q=" + Uri.EscapeDataString(value);
    }

    private bool RequestSitePermission(
        string uri,
        CoreWebView2PermissionKind permissionKind)
    {
        var host = Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            ? parsed.Host
            : uri;
        var label = permissionKind switch
        {
            CoreWebView2PermissionKind.Microphone => "麦克风",
            CoreWebView2PermissionKind.Camera => "摄像头",
            CoreWebView2PermissionKind.Geolocation => "位置",
            CoreWebView2PermissionKind.Notifications => "通知",
            CoreWebView2PermissionKind.ClipboardRead => "读取剪贴板",
            CoreWebView2PermissionKind.MultipleAutomaticDownloads => "连续下载文件",
            _ => permissionKind.ToString()
        };

        return MessageBox.Show(
            $"网站 {host} 请求使用“{label}”权限。\n\n是否允许本次请求？",
            "网站权限请求",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    private async Task DeleteAllLocalDataAsync()
    {
        await DeleteDirectoryWithRetryAsync(_session.Paths.WorkingProfile);
        await DeleteDirectoryWithRetryAsync(_session.Paths.DataRoot);
    }

    private static async Task DeleteDirectoryWithRetryAsync(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Exception? lastError = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException ex)
            {
                lastError = ex;
                await Task.Delay(150 * (attempt + 1));
            }
            catch (UnauthorizedAccessException ex)
            {
                lastError = ex;
                await Task.Delay(150 * (attempt + 1));
            }
        }

        throw new IOException($"无法删除本地数据目录“{path}”。", lastError);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_windowSource is not null)
        {
            _windowSource.RemoveHook(WindowMessageHook);
            _windowSource = null;
        }

        base.OnClosed(e);
    }
}
