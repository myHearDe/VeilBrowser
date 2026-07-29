using System.Windows;
using VeilBrowser.Core.Models;

namespace VeilBrowser.Views;

public partial class CredentialEditorWindow : Window
{
    public CredentialEditorWindow()
    {
        InitializeComponent();
    }

    public CredentialEntry? Entry { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SiteBox.Text) ||
            string.IsNullOrWhiteSpace(UserNameBox.Text) ||
            string.IsNullOrEmpty(PasswordBox.Password))
        {
            ErrorText.Text = "网站、用户名和密码都不能为空。";
            return;
        }

        Entry = new CredentialEntry
        {
            Site = SiteBox.Text.Trim(),
            UserName = UserNameBox.Text.Trim(),
            Password = PasswordBox.Password,
            UpdatedAt = DateTimeOffset.Now
        };
        DialogResult = true;
    }
}
