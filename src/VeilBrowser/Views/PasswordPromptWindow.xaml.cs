using System.Windows;
using System.Windows.Input;

namespace VeilBrowser.Views;

public partial class PasswordPromptWindow : Window
{
    private readonly AppSession _session;
    private int _failedAttempts;

    public PasswordPromptWindow(AppSession session, string areaName)
    {
        InitializeComponent();
        _session = session;
        HeadingText.Text = $"{areaName}已上锁";
        Loaded += (_, _) => PasswordBox.Focus();
    }

    private async void UnlockButton_Click(object sender, RoutedEventArgs e) => await VerifyAsync();

    private async void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await VerifyAsync();
        }
    }

    private async Task VerifyAsync()
    {
        UnlockButton.IsEnabled = false;
        ErrorText.Text = string.Empty;
        try
        {
            var verified = await _session.VerifyPasswordAsync(PasswordBox.Password);
            PasswordBox.Clear();
            if (verified)
            {
                DialogResult = true;
                return;
            }

            _failedAttempts++;
            var delaySeconds = Math.Min(15, 1 << Math.Min(_failedAttempts - 1, 4));
            ErrorText.Text = $"主密码不正确。{delaySeconds} 秒后可重试。";
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            ErrorText.Text = "主密码不正确，请重试。";
            UnlockButton.IsEnabled = true;
            PasswordBox.Focus();
        }
        catch (Exception ex)
        {
            PasswordBox.Clear();
            ErrorText.Text = $"验证失败：{ex.Message}";
            UnlockButton.IsEnabled = true;
            PasswordBox.Focus();
        }
    }
}
