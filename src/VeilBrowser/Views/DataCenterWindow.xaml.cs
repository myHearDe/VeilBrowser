using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VeilBrowser.Core.Models;

namespace VeilBrowser.Views;

public partial class DataCenterWindow : Window
{
    private readonly AppSession _session;
    private readonly Func<LockArea, string, bool> _ensureAccess;
    private readonly Action _scheduleSiteDataClear;
    private readonly Action<string> _openUrl;
    private readonly LockArea _initialArea;
    private LockArea _currentArea;

    public DataCenterWindow(
        AppSession session,
        Func<LockArea, string, bool> ensureAccess,
        Action scheduleSiteDataClear,
        Action<string> openUrl,
        LockArea initialArea = LockArea.History)
    {
        InitializeComponent();
        _session = session;
        _ensureAccess = ensureAccess;
        _scheduleSiteDataClear = scheduleSiteDataClear;
        _openUrl = openUrl;
        _initialArea = initialArea;
        _currentArea = initialArea;
        Loaded += (_, _) => OpenSection(_initialArea);
    }

    private void Section_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } &&
            Enum.TryParse<LockArea>(tag, out var area))
        {
            OpenSection(area);
        }
    }

    private void OpenSection(LockArea area)
    {
        var name = AreaName(area);
        if (!_ensureAccess(area, name))
        {
            return;
        }

        _currentArea = area;
        SectionTitle.Text = name;
        AddButton.Visibility = area is LockArea.Bookmarks or LockArea.Passwords
            ? Visibility.Visible
            : Visibility.Collapsed;
        OpenButton.Visibility = area is LockArea.History or LockArea.Downloads or
            LockArea.Bookmarks or LockArea.Sessions
            ? Visibility.Visible
            : Visibility.Collapsed;
        CopyButton.Visibility = area is LockArea.Bookmarks or LockArea.Passwords or
            LockArea.History or LockArea.Downloads
            ? Visibility.Visible
            : Visibility.Collapsed;
        DeleteButton.Visibility = area is LockArea.CookiesAndSiteData or LockArea.Autofill
            ? Visibility.Collapsed
            : Visibility.Visible;
        ClearButton.Visibility = area == LockArea.Autofill
            ? Visibility.Collapsed
            : Visibility.Visible;
        RefreshRows();
    }

    private void RefreshRows()
    {
        DataList.Items.Clear();
        SectionHint.Text = _currentArea switch
        {
            LockArea.History => $"共 {_session.State.History.Count} 条访问记录",
            LockArea.Downloads => $"共 {_session.State.Downloads.Count} 条下载记录；下载文件本身默认不加密",
            LockArea.Bookmarks => $"共 {_session.State.Bookmarks.Count} 个收藏",
            LockArea.Passwords => $"共 {_session.State.Credentials.Count} 个账号；密码仅在验证后复制",
            LockArea.CookiesAndSiteData => "Cookie、LocalStorage、IndexedDB 与缓存位于加密 WebView2 配置容器中",
            LockArea.Sessions => $"共 {_session.State.LastSessionUrls.Count} 个上次会话页面",
            LockArea.Autofill => "第一版不保存地址和银行卡自动填充资料",
            _ => string.Empty
        };

        foreach (var row in CreateRows())
        {
            DataList.Items.Add(row);
        }
    }

    private IEnumerable<DisplayRow> CreateRows()
    {
        return _currentArea switch
        {
            LockArea.History => _session.State.History
                .OrderByDescending(x => x.VisitedAt)
                .Select(x => new DisplayRow(
                    x,
                    x.Title,
                    x.Url,
                    x.VisitedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture))),
            LockArea.Downloads => _session.State.Downloads
                .OrderByDescending(x => x.StartedAt)
                .Select(x => new DisplayRow(
                    x,
                    x.FileName,
                    x.FullPath,
                    x.IsComplete ? "已完成" : x.IsCancelled ? "已取消" : "进行中")),
            LockArea.Bookmarks => _session.State.Bookmarks
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new DisplayRow(
                    x,
                    x.Title,
                    x.Url,
                    x.CreatedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture))),
            LockArea.Passwords => _session.State.Credentials
                .OrderBy(x => x.Site)
                .Select(x => new DisplayRow(x, x.Site, x.UserName, "••••••••")),
            LockArea.CookiesAndSiteData =>
            [
                new DisplayRow(null, "Edge WebView2 网站数据", "随整个配置目录加密", "退出后不可直接读取")
            ],
            LockArea.Sessions => _session.State.LastSessionUrls
                .Select(x => new DisplayRow(x, "上次打开的页面", x, string.Empty)),
            LockArea.Autofill =>
            [
                new DisplayRow(null, "未启用", "为降低敏感数据暴露面，第一版暂不保存", string.Empty)
            ],
            _ => []
        };
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_currentArea == LockArea.Passwords)
        {
            var editor = new CredentialEditorWindow { Owner = this };
            if (editor.ShowDialog() == true && editor.Entry is not null)
            {
                _session.State.Credentials.Add(editor.Entry);
                RefreshRows();
                if (!await TrySaveAsync("保存密码"))
                {
                    _session.State.Credentials.Remove(editor.Entry);
                    RefreshRows();
                }
            }
        }
        else if (_currentArea == LockArea.Bookmarks)
        {
            var editor = new BookmarkEditorWindow { Owner = this };
            if (editor.ShowDialog() == true && editor.Entry is not null)
            {
                _session.State.Bookmarks.Add(editor.Entry);
                RefreshRows();
                if (!await TrySaveAsync("保存收藏"))
                {
                    _session.State.Bookmarks.Remove(editor.Entry);
                    RefreshRows();
                }
            }
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (DataList.SelectedItem is not DisplayRow row)
        {
            return;
        }

        string? value = row.Source switch
        {
            CredentialEntry credential when _ensureAccess(LockArea.Passwords, "密码保险库") =>
                credential.Password,
            HistoryEntry history => history.Url,
            BookmarkEntry bookmark => bookmark.Url,
            DownloadEntry download => !string.IsNullOrWhiteSpace(download.FullPath)
                ? download.FullPath
                : download.Url,
            string text => text,
            _ => null
        };
        if (!string.IsNullOrWhiteSpace(value))
        {
            try
            {
                Clipboard.SetText(value);
                SectionHint.Text = "已复制到剪贴板。请使用后及时覆盖剪贴板内容。";
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                SectionHint.Text = "剪贴板暂时被其他程序占用，请稍后重试。";
            }
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e) => OpenSelectedItem();

    private void DataList_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        OpenSelectedItem();

    private void OpenSelectedItem()
    {
        if (DataList.SelectedItem is not DisplayRow row)
        {
            return;
        }

        switch (row.Source)
        {
            case DownloadEntry download
                when !string.IsNullOrWhiteSpace(download.FullPath) &&
                     File.Exists(download.FullPath):
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = download.FullPath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    SectionHint.Text = $"无法打开下载文件：{ex.Message}";
                }
                return;
            case DownloadEntry download when !string.IsNullOrWhiteSpace(download.Url):
                _openUrl(download.Url);
                Close();
                return;
            case HistoryEntry history:
                _openUrl(history.Url);
                Close();
                return;
            case BookmarkEntry bookmark:
                _openUrl(bookmark.Url);
                Close();
                return;
            case string sessionUrl:
                _openUrl(sessionUrl);
                Close();
                return;
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (DataList.SelectedItem is not DisplayRow row)
        {
            return;
        }

        var rollback = row.Source switch
        {
            HistoryEntry history => RemoveWithRollback(_session.State.History, history),
            DownloadEntry download => RemoveWithRollback(_session.State.Downloads, download),
            BookmarkEntry bookmark => RemoveWithRollback(_session.State.Bookmarks, bookmark),
            CredentialEntry credential => RemoveWithRollback(_session.State.Credentials, credential),
            string sessionUrl => RemoveWithRollback(_session.State.LastSessionUrls, sessionUrl),
            _ => null
        };
        RefreshRows();
        if (rollback is not null && !await TrySaveAsync("删除记录"))
        {
            rollback();
            RefreshRows();
        }
    }

    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
            $"确定清空“{AreaName(_currentArea)}”吗？此操作无法撤销。",
            "确认清空",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        Action? rollback = null;
        switch (_currentArea)
        {
            case LockArea.History:
                rollback = ClearWithRollback(_session.State.History);
                _session.State.History.Clear();
                break;
            case LockArea.Downloads:
                rollback = ClearWithRollback(_session.State.Downloads);
                _session.State.Downloads.Clear();
                break;
            case LockArea.Bookmarks:
                rollback = ClearWithRollback(_session.State.Bookmarks);
                _session.State.Bookmarks.Clear();
                break;
            case LockArea.Passwords:
                rollback = ClearWithRollback(_session.State.Credentials);
                _session.State.Credentials.Clear();
                break;
            case LockArea.Sessions:
                rollback = ClearWithRollback(_session.State.LastSessionUrls);
                _session.State.LastSessionUrls.Clear();
                break;
            case LockArea.CookiesAndSiteData:
                _scheduleSiteDataClear();
                SectionHint.Text =
                    "已安排清理：关闭浏览器时将删除 Cookie、缓存、LocalStorage、IndexedDB 和站点权限。";
                break;
        }
        RefreshRows();
        if (rollback is not null && !await TrySaveAsync("清空数据"))
        {
            rollback();
            RefreshRows();
        }
    }

    private async Task<bool> TrySaveAsync(string operation)
    {
        try
        {
            await _session.SaveAsync();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"{operation}失败：{ex.Message}",
                "无法保存浏览器数据",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private static Action? RemoveWithRollback<T>(List<T> items, T item)
    {
        var index = items.IndexOf(item);
        if (index < 0)
        {
            return null;
        }

        items.RemoveAt(index);
        return () => items.Insert(Math.Min(index, items.Count), item);
    }

    private static Action ClearWithRollback<T>(List<T> items)
    {
        var snapshot = items.ToList();
        return () => items.AddRange(snapshot);
    }

    private static string AreaName(LockArea area) => area switch
    {
        LockArea.History => "浏览历史",
        LockArea.Downloads => "下载记录",
        LockArea.Bookmarks => "书签收藏",
        LockArea.Passwords => "密码保险库",
        LockArea.CookiesAndSiteData => "Cookie 与网站数据",
        LockArea.Sessions => "会话恢复记录",
        LockArea.Autofill => "自动填充资料",
        LockArea.Settings => "安全设置",
        _ => "浏览器"
    };

    private sealed record DisplayRow(object? Source, string Name, string Detail, string Meta);
}
