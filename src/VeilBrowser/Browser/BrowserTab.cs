using System.Drawing;
using System.IO;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace VeilBrowser.Browser;

public sealed class BrowserTab : IDisposable
{
    private readonly CoreWebView2Environment _environment;
    private readonly BrowserDownloadHandler _downloadHandler;
    private readonly BrowserExtensionManager _extensionManager;
    private bool _trackingProtectionEnabled;
    private readonly Action<string> _openInNewTab;
    private readonly Action<bool> _fullscreenChanged;
    private readonly Func<string, CoreWebView2PermissionKind, bool> _requestPermission;
    private bool _initialized;

    public BrowserTab(
        CoreWebView2Environment environment,
        BrowserDownloadHandler downloadHandler,
        BrowserExtensionManager extensionManager,
        bool trackingProtectionEnabled,
        Action<string> openInNewTab,
        Action<bool> fullscreenChanged,
        Func<string, CoreWebView2PermissionKind, bool> requestPermission)
    {
        _environment = environment;
        _downloadHandler = downloadHandler;
        _extensionManager = extensionManager;
        _trackingProtectionEnabled = trackingProtectionEnabled;
        _openInNewTab = openInNewTab;
        _fullscreenChanged = fullscreenChanged;
        _requestPermission = requestPermission;
        Browser = new WebView2
        {
            DefaultBackgroundColor = Color.White
        };
    }

    public event EventHandler<string>? TitleChanged;
    public event EventHandler<string>? AddressChanged;
    public event EventHandler<bool>? LoadingStateChanged;
    public event EventHandler<string>? StatusMessageChanged;

    public WebView2 Browser { get; }
    public string Title { get; private set; } = "新标签页";
    public string Address => Browser.CoreWebView2?.Source ?? Browser.Source?.AbsoluteUri ?? string.Empty;
    public bool CanGoBack => Browser.CanGoBack;
    public bool CanGoForward => Browser.CanGoForward;
    public bool IsDisposed { get; private set; }

    public async Task InitializeAsync(string address)
    {
        if (_initialized)
        {
            Navigate(address);
            return;
        }

        await Browser.EnsureCoreWebView2Async(_environment);
        var core = Browser.CoreWebView2;
        var newTabAssets = Path.Combine(AppContext.BaseDirectory, "Assets", "NewTab");
        if (Directory.Exists(newTabAssets))
        {
            core.SetVirtualHostNameToFolderMapping(
                "veil.local",
                newTabAssets,
                CoreWebView2HostResourceAccessKind.DenyCors);
        }
        await _extensionManager.EnsureInstalledAsync(core);
        var settings = core.Settings;
        settings.AreBrowserAcceleratorKeysEnabled = true;
        settings.AreDefaultContextMenusEnabled = true;
        settings.AreDevToolsEnabled = true;
        settings.AreHostObjectsAllowed = false;
        settings.IsBuiltInErrorPageEnabled = true;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsPinchZoomEnabled = true;
        settings.IsStatusBarEnabled = false;
        settings.IsSwipeNavigationEnabled = true;
        settings.IsZoomControlEnabled = true;

        core.Profile.PreferredTrackingPreventionLevel = _trackingProtectionEnabled
            ? CoreWebView2TrackingPreventionLevel.Strict
            : CoreWebView2TrackingPreventionLevel.None;

        core.DocumentTitleChanged += (_, _) =>
        {
            Title = string.IsNullOrWhiteSpace(core.DocumentTitle)
                ? "新标签页"
                : core.DocumentTitle;
            TitleChanged?.Invoke(this, Title);
        };
        core.SourceChanged += (_, _) => AddressChanged?.Invoke(this, core.Source);
        core.NavigationStarting += (_, e) =>
        {
            if (!IsAllowedNavigation(e.Uri))
            {
                e.Cancel = true;
                StatusMessageChanged?.Invoke(this, $"已拦截不安全或不支持的导航：{e.Uri}");
                return;
            }

            LoadingStateChanged?.Invoke(this, true);
        };
        core.NavigationCompleted += (_, _) => LoadingStateChanged?.Invoke(this, false);
        core.StatusBarTextChanged += (_, _) =>
            StatusMessageChanged?.Invoke(this, core.StatusBarText ?? string.Empty);
        core.PermissionRequested += (_, e) =>
        {
            e.State = _requestPermission(e.Uri, e.PermissionKind)
                ? CoreWebView2PermissionState.Allow
                : CoreWebView2PermissionState.Deny;
        };
        core.ServerCertificateErrorDetected += (_, e) =>
        {
            e.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
            StatusMessageChanged?.Invoke(
                this,
                $"已阻止证书异常页面：{e.RequestUri} ({e.ErrorStatus})");
        };
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (TryGetSafeExternalUri(e.Uri, out var newWindowUri))
            {
                _openInNewTab(newWindowUri);
            }
        };
        core.ContainsFullScreenElementChanged += (_, _) =>
            _fullscreenChanged(core.ContainsFullScreenElement);
        core.DownloadStarting += _downloadHandler.HandleDownloadStarting;
        core.ContextMenuRequested += Core_ContextMenuRequested;

        _initialized = true;
        Navigate(address);
    }

    public void Navigate(string address)
    {
        if (IsDisposed)
        {
            return;
        }

        if (Browser.CoreWebView2 is { } core)
        {
            core.Navigate(address);
        }
    }

    public void Back()
    {
        if (CanGoBack)
        {
            Browser.GoBack();
        }
    }

    public void Forward()
    {
        if (CanGoForward)
        {
            Browser.GoForward();
        }
    }

    public void Reload() => Browser.Reload();

    public void Print() => Browser.CoreWebView2?.ShowPrintUI();

    public void ShowDevTools() => Browser.CoreWebView2?.OpenDevToolsWindow();

    public void ViewSource()
    {
        if (!string.IsNullOrWhiteSpace(Address))
        {
            _openInNewTab("view-source:" + Address);
        }
    }

    public void ZoomIn() => Browser.ZoomFactor = Math.Min(5.0, Browser.ZoomFactor + 0.1);

    public void ZoomOut() => Browser.ZoomFactor = Math.Max(0.25, Browser.ZoomFactor - 0.1);

    public void ResetZoom() => Browser.ZoomFactor = 1.0;

    public void SetTrackingProtection(bool enabled)
    {
        _trackingProtectionEnabled = enabled;
        if (Browser.CoreWebView2 is { } core)
        {
            core.Profile.PreferredTrackingPreventionLevel = enabled
                ? CoreWebView2TrackingPreventionLevel.Strict
                : CoreWebView2TrackingPreventionLevel.None;
        }
    }

    public async Task FindAsync(string query)
    {
        if (Browser.CoreWebView2 is not { } core || string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        var options = _environment.CreateFindOptions();
        options.FindTerm = query;
        options.IsCaseSensitive = false;
        options.ShouldHighlightAllMatches = true;
        options.ShouldMatchWord = false;
        options.SuppressDefaultFindDialog = false;
        await core.Find.StartAsync(options);
    }

    public Task ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds kinds) =>
        Browser.CoreWebView2?.Profile.ClearBrowsingDataAsync(kinds) ?? Task.CompletedTask;

    private void Core_ContextMenuRequested(
        object? sender,
        CoreWebView2ContextMenuRequestedEventArgs e)
    {
        var target = e.ContextMenuTarget;
        if (!target.HasLinkUri || string.IsNullOrWhiteSpace(target.LinkUri))
        {
            return;
        }

        var link = target.LinkUri;
        var openInTab = _environment.CreateContextMenuItem(
            "在新标签页打开链接",
            iconStream: null,
            CoreWebView2ContextMenuItemKind.Command);
        openInTab.CustomItemSelected += (_, _) => _openInNewTab(link);
        e.MenuItems.Insert(0, openInTab);
    }

    private static bool TryGetSafeExternalUri(string? input, out string uriText)
    {
        uriText = string.Empty;
        if (string.IsNullOrWhiteSpace(input) ||
            !Uri.TryCreate(input, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https" or "about"))
        {
            return false;
        }

        uriText = uri.AbsoluteUri;
        return true;
    }

    private static bool IsAllowedNavigation(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) ||
            !Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme is "http" or "https")
        {
            return true;
        }

        if (uri.Scheme == "about" &&
            string.Equals(input, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (uri.Scheme == "chrome-extension")
        {
            return true;
        }

        if (input.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(input["view-source:".Length..], UriKind.Absolute, out var sourceUri) &&
            sourceUri.Scheme is "http" or "https")
        {
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        Browser.Dispose();
        IsDisposed = true;
    }
}
