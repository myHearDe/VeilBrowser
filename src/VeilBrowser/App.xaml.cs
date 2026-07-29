using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using VeilBrowser.Core.Models;
using VeilBrowser.Core.Security;
using VeilBrowser.Infrastructure;
using VeilBrowser.Security;
using VeilBrowser.Views;

namespace VeilBrowser;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "WPF owns the Application lifetime; AppSession is disposed in OnExit.")]
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\VeilBrowser.SingleInstance";
    private AppSession? _session;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            _singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                SingleInstanceMutexName,
                out _ownsSingleInstanceMutex);
            if (!_ownsSingleInstanceMutex)
            {
                MessageBox.Show(
                    "隐栈浏览器已经在运行。\n\n为避免两个进程同时读写加密资料，请先使用已打开的窗口。",
                    "浏览器已运行",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

            var paths = new AppPaths();
            var security = new SecurityBootstrapService(paths);
            var gate = new StartupGateWindow(security);
            if (gate.ShowDialog() != true || gate.MasterKey is null)
            {
                Shutdown();
                return;
            }

            var key = gate.MasterKey;
            if (Directory.Exists(paths.WorkingProfile))
            {
                await ProfileContainerService.ProtectAsync(
                    paths.WorkingProfile, paths.EncryptedProfile, key);
            }
            await ProfileContainerService.RestoreAsync(
                paths.EncryptedProfile, paths.WorkingProfile, key);

            var stateStore = new EncryptedJsonStore<BrowserState>(paths.EncryptedState);
            var state = await stateStore.LoadAsync(key);
            var metadata = await security.ReadMetadataAsync();
            if (metadata?.Mode == KeyProtectionMode.WindowsAccount)
            {
                state.Preferences.StartupLock = false;
                foreach (var area in Enum.GetValues<LockArea>())
                {
                    state.Preferences.AreaLocks[area] = false;
                }
            }
            var webViewEnvironment = await CreateWebViewEnvironmentAsync(
                paths,
                state.Preferences);
            _session = new AppSession(
                paths,
                security,
                key,
                state,
                metadata?.Mode == KeyProtectionMode.MasterPassword);

            var mainWindow = new BrowserWindow(_session, webViewEnvironment);
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"隐栈浏览器无法启动。\n\n{ex.Message}",
                "启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            _session?.Dispose();
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _session?.Dispose();
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static Task<CoreWebView2Environment> CreateWebViewEnvironmentAsync(
        AppPaths paths,
        SecurityPreferences preferences)
    {
        var arguments = new List<string>
        {
            "--autoplay-policy=no-user-gesture-required",
            "--enable-features=msWebView2EnableDownloadContentInWebResourceResponseReceived"
        };
        if (preferences.WebRtcLeakProtection)
        {
            arguments.Add("--force-webrtc-ip-handling-policy=disable_non_proxied_udp");
        }
        if (preferences.BlockThirdPartyCookies)
        {
            arguments.Add("--block-third-party-cookies");
        }

        var options = new CoreWebView2EnvironmentOptions(
            string.Join(' ', arguments),
            "zh-CN",
            targetCompatibleBrowserVersion: null,
            allowSingleSignOnUsingOSPrimaryAccount: false,
            customSchemeRegistrations: [])
        {
            AreBrowserExtensionsEnabled = true,
            ScrollBarStyle = CoreWebView2ScrollbarStyle.FluentOverlay
        };
        var userDataFolder = Path.Combine(paths.WorkingProfile, "WebView2");
        return CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder,
            options);
    }
}
