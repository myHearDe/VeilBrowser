using System.Windows;
using VeilBrowser.Core.Models;

namespace VeilBrowser.Views;

public partial class BookmarkEditorWindow : Window
{
    public BookmarkEditorWindow()
    {
        InitializeComponent();
    }

    public BookmarkEntry? Entry { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text) ||
            !Uri.TryCreate(UrlBox.Text.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            ErrorText.Text = "请输入名称以及有效的 http/https 网址。";
            return;
        }

        Entry = new BookmarkEntry(
            Guid.NewGuid(),
            TitleBox.Text.Trim(),
            uri.AbsoluteUri,
            DateTimeOffset.Now);
        DialogResult = true;
    }
}
