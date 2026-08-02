using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using VeilBrowser.Core.Models;
using VeilBrowser.Infrastructure;
using VeilBrowser.Security;

namespace VeilBrowser.Views;

public partial class SecuritySettingsWindow : Window
{
    private readonly AppSession _session;
    private SecurityMetadata? _metadata;
    private BrowserTheme _originalTheme;

    public SecuritySettingsWindow(AppSession session)
    {
        InitializeComponent();
        _session = session;
        _originalTheme = session.State.Preferences.Theme;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _metadata = await _session.Security.ReadMetadataAsync();
            var preferences = _session.State.Preferences;
            _originalTheme = preferences.Theme;
            StartupLockCheck.IsChecked = _metadata?.StartupLock == true;
            AutoLockBox.Text = preferences.AutoLockMinutes.ToString(CultureInfo.CurrentCulture);
            ThirdPartyCookiesCheck.IsChecked = preferences.BlockThirdPartyCookies;
            TrackingProtectionCheck.IsChecked = preferences.TrackingProtectionEnabled;
            WebRtcCheck.IsChecked = preferences.WebRtcLeakProtection;
            ClearCacheCheck.IsChecked = preferences.ClearCacheOnExit;
            HomePageBox.Text = preferences.HomePage;
            SelectTheme(preferences.Theme);

            var passwordMode = _metadata?.Mode == KeyProtectionMode.MasterPassword;
            KeyModeText.Text = passwordMode
                ? "当前使用主密码包裹随机数据密钥。更改密码不需要重加密全部浏览文件。"
                : "当前没有浏览器主密码，随机数据密钥由 Windows 当前账户保护。你可以在下方设置主密码。";
            PasswordPanelTitle.Text = passwordMode ? "更改主密码" : "设置主密码";
            StartupLockCheck.IsEnabled = passwordMode;
            AreaLockHint.Text = passwordMode
                ? "打开这些页面前再次验证主密码。"
                : "设置主密码后才能启用区域锁。";

            foreach (var checkBox in FindAreaCheckBoxes())
            {
                if (Enum.TryParse<LockArea>(checkBox.Tag?.ToString(), out var area))
                {
                    checkBox.IsChecked = passwordMode && preferences.IsLocked(area);
                    checkBox.IsEnabled = passwordMode;
                }
            }
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"无法读取安全设置：{ex.Message}";
            SaveButton.IsEnabled = false;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (!int.TryParse(AutoLockBox.Text, out var autoLockMinutes) ||
            autoLockMinutes is < 0 or > 1440)
        {
            ErrorText.Text = "自动锁定时间必须是 0 到 1440 之间的整数。";
            return;
        }

        if (!Uri.TryCreate(HomePageBox.Text.Trim(), UriKind.Absolute, out var homePage) ||
            homePage.Scheme is not ("http" or "https"))
        {
            ErrorText.Text = "主页必须是有效的 http/https 网址。";
            return;
        }

        var wantsNewPassword = !string.IsNullOrEmpty(NewPasswordBox.Password);
        if (wantsNewPassword)
        {
            if (NewPasswordBox.Password.Length < 12)
            {
                ErrorText.Text = "新主密码至少需要 12 个字符，并同时包含字母和数字。";
                return;
            }
            if (!NewPasswordBox.Password.Any(char.IsLetter) ||
                !NewPasswordBox.Password.Any(char.IsDigit))
            {
                ErrorText.Text = "新主密码至少需要包含一个字母和一个数字。";
                return;
            }
            if (!string.Equals(
                NewPasswordBox.Password,
                ConfirmPasswordBox.Password,
                StringComparison.Ordinal))
            {
                ErrorText.Text = "两次输入的新主密码不一致。";
                return;
            }

            if (_metadata?.Mode == KeyProtectionMode.MasterPassword)
            {
                var prompt = new PasswordPromptWindow(_session, "更改主密码") { Owner = this };
                if (prompt.ShowDialog() != true)
                {
                    return;
                }
            }
        }

        SaveButton.IsEnabled = false;
        try
        {
            var startupLock = StartupLockCheck.IsChecked == true;
            if (wantsNewPassword)
            {
                startupLock = _metadata?.Mode == KeyProtectionMode.WindowsAccount
                    ? true
                    : startupLock;
                await _session.Security.SetOrChangeMasterPasswordAsync(
                    NewPasswordBox.Password,
                    startupLock,
                    _session.MasterKey);
                _session.MarkMasterPasswordConfigured();
                _metadata = await _session.Security.ReadMetadataAsync();
            }
            else if (_metadata?.Mode == KeyProtectionMode.MasterPassword)
            {
                await _session.Security.UpdateStartupLockAsync(
                    startupLock, _session.MasterKey);
            }

            var preferences = _session.State.Preferences;
            preferences.StartupLock = startupLock;
            preferences.AutoLockMinutes = autoLockMinutes;
            preferences.BlockThirdPartyCookies = ThirdPartyCookiesCheck.IsChecked == true;
            preferences.TrackingProtectionEnabled = TrackingProtectionCheck.IsChecked == true;
            preferences.WebRtcLeakProtection = WebRtcCheck.IsChecked == true;
            preferences.ClearCacheOnExit = ClearCacheCheck.IsChecked == true;
            preferences.HomePage = homePage.AbsoluteUri;
            preferences.Theme = GetSelectedTheme();

            var passwordMode = _metadata?.Mode == KeyProtectionMode.MasterPassword;
            foreach (var checkBox in FindAreaCheckBoxes())
            {
                if (Enum.TryParse<LockArea>(checkBox.Tag?.ToString(), out var area))
                {
                    preferences.AreaLocks[area] = passwordMode && checkBox.IsChecked == true;
                }
            }

            await _session.SaveAsync();
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"保存失败：{ex.Message}";
            SaveButton.IsEnabled = true;
        }
    }

    private void NewPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_metadata?.Mode != KeyProtectionMode.WindowsAccount)
        {
            return;
        }

        var willEnablePasswordMode = !string.IsNullOrEmpty(NewPasswordBox.Password);
        StartupLockCheck.IsEnabled = willEnablePasswordMode;
        if (willEnablePasswordMode)
        {
            StartupLockCheck.IsChecked = true;
        }

        AreaLockHint.Text = willEnablePasswordMode
            ? "选择设置主密码后要再次验证的区域。"
            : "设置主密码后才能启用区域锁。";
        foreach (var checkBox in FindAreaCheckBoxes())
        {
            checkBox.IsEnabled = willEnablePasswordMode;
        }
    }

    private IEnumerable<CheckBox> FindAreaCheckBoxes() =>
        AreaLocksGrid.Children.OfType<CheckBox>();

    private void SelectTheme(BrowserTheme theme)
    {
        MidnightThemeRadio.IsChecked = theme == BrowserTheme.MidnightEmerald;
        DaylightThemeRadio.IsChecked = theme == BrowserTheme.PorcelainDaylight;
        GraphiteThemeRadio.IsChecked = theme == BrowserTheme.GraphiteFocus;
    }

    private BrowserTheme GetSelectedTheme()
    {
        var selected = new[]
        {
            MidnightThemeRadio,
            DaylightThemeRadio,
            GraphiteThemeRadio
        }.FirstOrDefault(radio => radio.IsChecked == true);

        return Enum.TryParse<BrowserTheme>(selected?.Tag?.ToString(), out var theme)
            ? theme
            : BrowserTheme.MidnightEmerald;
    }

    private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || sender is not RadioButton radio ||
            !Enum.TryParse<BrowserTheme>(radio.Tag?.ToString(), out var theme))
        {
            return;
        }

        ThemeManager.Apply(theme);
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        if (DialogResult != true)
        {
            ThemeManager.Apply(_originalTheme);
        }
    }
}
