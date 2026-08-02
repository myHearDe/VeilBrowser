using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace VeilBrowser.Browser;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The semaphore lives for the browser window lifetime.")]
public sealed class BrowserExtensionManager
{
    private const string AdGuardName = "AdGuard";
    private const string IntegrationVersion = "1.1.0";
    private const int MinimumExtensionRuntimeMajor = 121;
    private readonly string _extensionFolder;
    private readonly string _installMarkerPath;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private CoreWebView2BrowserExtension? _adGuard;

    public BrowserExtensionManager(
        string extensionFolder,
        string installMarkerPath)
    {
        _extensionFolder = extensionFolder;
        _installMarkerPath = installMarkerPath;
    }

    public event EventHandler? StateChanged;

    public bool IsReady => _adGuard is not null;
    public bool IsEnabled => _adGuard?.IsEnabled == true;
    public string DisplayName => _adGuard?.Name ?? "AdGuard 广告拦截器";
    public string RuntimeVersion { get; private set; } = "未知";
    public string? ErrorMessage { get; private set; }

    public async Task EnsureInstalledAsync(CoreWebView2 core)
    {
        if (_adGuard is not null)
        {
            return;
        }

        await _initializeLock.WaitAsync();
        try
        {
            if (_adGuard is not null)
            {
                return;
            }

            RuntimeVersion = core.Environment.BrowserVersionString;
            if (!TryGetRuntimeMajor(RuntimeVersion, out var runtimeMajor) ||
                runtimeMajor < MinimumExtensionRuntimeMajor)
            {
                ErrorMessage =
                    $"当前 WebView2 内核 {RuntimeVersion} 太旧，AdGuard 5.4.3.1 " +
                    $"至少需要 Chromium {MinimumExtensionRuntimeMajor}。请更新 Microsoft Edge WebView2 Runtime。";
                return;
            }

            var manifestPath = Path.Combine(_extensionFolder, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                ErrorMessage = $"找不到内置扩展：{manifestPath}";
                return;
            }

            using var manifest = JsonDocument.Parse(
                await File.ReadAllTextAsync(manifestPath));
            var version = manifest.RootElement
                .GetProperty("version")
                .GetString() ?? "unknown";
            var desiredInstall = new ExtensionInstallMarker(
                Path.GetFullPath(_extensionFolder),
                version,
                IntegrationVersion);
            var existingMarker = await ReadInstallMarkerAsync();

            var extensions = await core.Profile.GetBrowserExtensionsAsync();
            _adGuard = extensions.FirstOrDefault(
                extension => extension.Name.Contains(
                    AdGuardName,
                    StringComparison.OrdinalIgnoreCase));
            if (_adGuard is null || existingMarker != desiredInstall)
            {
                _adGuard = await core.Profile.AddBrowserExtensionAsync(_extensionFolder);
                await WriteInstallMarkerAsync(desiredInstall);
            }
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            _initializeLock.Release();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        if (_adGuard is null)
        {
            return;
        }

        await _adGuard.EnableAsync(enabled);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public string? GetPageUrl(string page)
    {
        if (_adGuard is null)
        {
            return null;
        }

        return $"chrome-extension://{_adGuard.Id}/pages/{page.TrimStart('/')}";
    }

    private static bool TryGetRuntimeMajor(string version, out int major)
    {
        var separator = version.IndexOf('.');
        var majorText = separator > 0 ? version[..separator] : version;
        return int.TryParse(majorText, out major);
    }

    private async Task<ExtensionInstallMarker?> ReadInstallMarkerAsync()
    {
        if (!File.Exists(_installMarkerPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ExtensionInstallMarker>(
                await File.ReadAllTextAsync(_installMarkerPath));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task WriteInstallMarkerAsync(ExtensionInstallMarker marker)
    {
        var directory = Path.GetDirectoryName(_installMarkerPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(
            _installMarkerPath,
            JsonSerializer.Serialize(marker));
    }

    private sealed record ExtensionInstallMarker(
        string Path,
        string Version,
        string IntegrationVersion);
}
