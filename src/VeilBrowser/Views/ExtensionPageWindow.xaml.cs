using System.Diagnostics.CodeAnalysis;
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
    private readonly Action<string> _openInTab;
    private WebView2? _webView;

    public ExtensionPageWindow(
        CoreWebView2Environment environment,
        string address,
        Action<string> openInTab)
    {
        InitializeComponent();
        _environment = environment;
        _address = address;
        _openInTab = openInTab;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _webView = new WebView2();
            ExtensionHost.Children.Add(_webView);
            await _webView.EnsureCoreWebView2Async(_environment);
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _webView.CoreWebView2.Settings.AreHostObjectsAllowed = false;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.NavigationStarting += Core_NavigationStarting;
            _webView.CoreWebView2.NewWindowRequested += Core_NewWindowRequested;
            _webView.CoreWebView2.WebMessageReceived += Core_WebMessageReceived;
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

    private void Core_WebMessageReceived(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (IsExpectedExtensionOrigin(e.Source) &&
            string.Equals(e.TryGetWebMessageAsString(), "close", StringComparison.Ordinal))
        {
            Close();
        }
    }

    private void Core_NavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        if (!IsExpectedExtensionOrigin(e.Uri))
        {
            e.Cancel = true;
        }
    }

    private void Core_NewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (TryGetSafeWebUri(e.Uri, out var uri))
        {
            _openInTab(uri);
            Close();
        }
    }

    private bool IsExpectedExtensionOrigin(string? input)
    {
        return Uri.TryCreate(_address, UriKind.Absolute, out var expected) &&
               Uri.TryCreate(input, UriKind.Absolute, out var actual) &&
               expected.Scheme == "chrome-extension" &&
               actual.Scheme == expected.Scheme &&
               string.Equals(actual.Host, expected.Host, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetSafeWebUri(string? input, out string uriText)
    {
        uriText = string.Empty;
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        uriText = uri.AbsoluteUri;
        return true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closed(object? sender, EventArgs e)
    {
        _webView?.Dispose();
        _webView = null;
    }
}
