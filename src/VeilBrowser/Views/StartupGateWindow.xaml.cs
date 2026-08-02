using System.Windows;
using System.Windows.Input;
using VeilBrowser.Security;

namespace VeilBrowser.Views;

public partial class StartupGateWindow : Window
{
    private readonly SecurityBootstrapService _security;
    private bool _configured;
    private bool _working;
    private int _failedAttempts;

    public StartupGateWindow(SecurityBootstrapService security)
    {
        InitializeComponent();
        _security = security;
    }

    public byte[]? MasterKey { get; private set; }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _configured = _security.IsConfigured;
        if (!_configured)
        {
            HeroTitleText.Text = "创建加密空间";
            SetupPanel.Visibility = Visibility.Visible;
            UpdateModeUi();
            return;
        }

        HeroTitleText.Text = "欢迎回来";
        SetBusy(true, "正在使用 Windows 安全保护解锁…");
        try
        {
            MasterKey = await _security.TryUnlockWithoutPasswordAsync();
            if (MasterKey is not null)
            {
                DialogResult = true;
                return;
            }

            SetBusy(false);
            UnlockPanel.Visibility = Visibility.Visible;
            PrimaryButton.Content = "解锁";
            UnlockPasswordBox.Focus();
        }
        catch (Exception ex)
        {
            SetBusy(false);
            UnlockPanel.Visibility = Visibility.Visible;
            ErrorText.Text = $"无法读取安全配置：{ex.Message}";
        }
    }

    private void ModeRadio_Changed(object sender, RoutedEventArgs e) => UpdateModeUi();

    private void UpdateModeUi()
    {
        if (PasswordSetupPanel is null || PasswordModeRadio is null)
        {
            return;
        }

        PasswordSetupPanel.Visibility = PasswordModeRadio.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_working)
        {
            return;
        }

        if (_configured)
        {
            await UnlockAsync();
        }
        else
        {
            await ConfigureAsync();
        }
    }

    private async Task ConfigureAsync()
    {
        ErrorText.Text = string.Empty;
        if (PasswordModeRadio.IsChecked == true)
        {
            if (NewPasswordBox.Password.Length < 12)
            {
                ErrorText.Text = "主密码至少需要 12 个字符，并同时包含字母和数字。";
                return;
            }

            if (!NewPasswordBox.Password.Any(char.IsLetter) ||
                !NewPasswordBox.Password.Any(char.IsDigit))
            {
                ErrorText.Text = "主密码至少需要包含一个字母和一个数字。";
                return;
            }

            if (!string.Equals(
                NewPasswordBox.Password,
                ConfirmPasswordBox.Password,
                StringComparison.Ordinal))
            {
                ErrorText.Text = "两次输入的主密码不一致。";
                return;
            }
        }

        SetBusy(true, "正在创建加密密钥…");
        try
        {
            MasterKey = PasswordModeRadio.IsChecked == true
                ? await _security.ConfigurePasswordAsync(
                    NewPasswordBox.Password,
                    StartupLockCheck.IsChecked == true)
                : await _security.ConfigureWindowsAccountAsync();
            DialogResult = true;
        }
        catch (Exception ex)
        {
            SetBusy(false);
            SetupPanel.Visibility = Visibility.Visible;
            ErrorText.Text = $"安全设置失败：{ex.Message}";
        }
    }

    private async Task UnlockAsync()
    {
        if (string.IsNullOrEmpty(UnlockPasswordBox.Password))
        {
            ErrorText.Text = "请输入主密码。";
            return;
        }

        SetBusy(true, "正在用 Argon2id 验证并解密…");
        try
        {
            MasterKey = await _security.UnlockWithPasswordAsync(UnlockPasswordBox.Password);
            UnlockPasswordBox.Clear();
            if (MasterKey is null)
            {
                _failedAttempts++;
                var delaySeconds = Math.Min(30, 1 << Math.Min(_failedAttempts - 1, 5));
                ErrorText.Text = $"主密码不正确。{delaySeconds} 秒后可重试。";
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                SetBusy(false);
                UnlockPanel.Visibility = Visibility.Visible;
                ErrorText.Text = "主密码不正确，请重试。";
                UnlockPasswordBox.Focus();
                return;
            }

            DialogResult = true;
        }
        catch (Exception ex)
        {
            SetBusy(false);
            UnlockPanel.Visibility = Visibility.Visible;
            ErrorText.Text = $"解锁失败：{ex.Message}";
        }
    }

    private async void UnlockPasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await UnlockAsync();
        }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _working = busy;
        SetupPanel.Visibility = Visibility.Collapsed;
        UnlockPanel.Visibility = Visibility.Collapsed;
        BusyPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        PrimaryButton.IsEnabled = !busy;
        if (message is not null)
        {
            BusyText.Text = message;
        }
    }
}
