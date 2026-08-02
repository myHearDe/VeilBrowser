using System.Windows;
using VeilBrowser.Core.Models;

namespace VeilBrowser.Views;

public partial class BookmarkEditorWindow : Window
{
    private readonly BookmarkEntry? _existingEntry;

    public BookmarkEditorWindow(
        string title = "",
        string url = "",
        BookmarkEntry? existingEntry = null)
    {
        InitializeComponent();
        _existingEntry = existingEntry;
        TitleBox.Text = existingEntry?.Title ?? title;
        UrlBox.Text = existingEntry?.Url ?? url;
        if (existingEntry is not null)
        {
            Title = "编辑收藏";
            HeadingText.Text = "编辑收藏";
            DeleteButton.Visibility = Visibility.Visible;
        }

        Loaded += (_, _) =>
        {
            TitleBox.Focus();
            TitleBox.SelectAll();
        };
    }

    public BookmarkEntry? Entry { get; private set; }
    public bool DeleteRequested { get; private set; }

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
            _existingEntry?.Id ?? Guid.NewGuid(),
            TitleBox.Text.Trim(),
            uri.AbsoluteUri,
            _existingEntry?.CreatedAt ?? DateTimeOffset.Now);
        DialogResult = true;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        DeleteRequested = true;
        DialogResult = true;
    }
}
