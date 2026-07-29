using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace VeilBrowser.Views;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The WPF Closed event disposes the hosted WebView2 control.")]
public partial class ExtensionPageWindow : Window
{
    private readonly CoreWebView2Environment _environment;
    private readonly string _address;
    private readonly string? _targetAddress;
    private readonly Action<string> _openInTab;
    private WebView2? _webView;

    public ExtensionPageWindow(
        CoreWebView2Environment environment,
        string address,
        string? targetAddress,
        Action<string> openInTab)
    {
        InitializeComponent();
        _environment = environment;
        _address = address;
        _targetAddress = targetAddress;
        _openInTab = openInTab;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _webView = new WebView2();
            ExtensionHost.Children.Add(_webView);
            await _webView.EnsureCoreWebView2Async(_environment);
            if (!string.IsNullOrWhiteSpace(_targetAddress))
            {
                var targetJson = JsonSerializer.Serialize(_targetAddress);
                await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    $$"""
                    (() => {
                        const targetUrl = {{targetJson}};
                        const tabsApi = globalThis.chrome?.tabs;
                        if (!tabsApi?.query || !targetUrl) {
                            return;
                        }
                        const originalQuery = tabsApi.query.bind(tabsApi);
                        tabsApi.query = async (queryInfo) => {
                            if (queryInfo?.active && queryInfo?.currentWindow) {
                                const allTabs = await originalQuery({});
                                const targetTab = allTabs.find(
                                    tab => tab.url === targetUrl ||
                                        tab.pendingUrl === targetUrl);
                                if (targetTab) {
                                    return [targetTab];
                                }
                            }
                            return originalQuery(queryInfo);
                        };
                    })();
                    """);
            }
            _webView.CoreWebView2.NewWindowRequested += Core_NewWindowRequested;
            _webView.CoreWebView2.Navigate(_address);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"无法打开 AdGuard 控制面板：{ex.Message}",
                "AdGuard",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
    }

    private void Core_NewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (!string.IsNullOrWhiteSpace(e.Uri))
        {
            _openInTab(e.Uri);
            Close();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closed(object? sender, EventArgs e)
    {
        _webView?.Dispose();
        _webView = null;
    }
}
