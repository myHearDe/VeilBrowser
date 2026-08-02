namespace VeilBrowser.Core.Models;

public sealed class BrowserState
{
    public SecurityPreferences Preferences { get; set; } = new();
    public List<HistoryEntry> History { get; set; } = [];
    public List<BookmarkEntry> Bookmarks { get; set; } = [];
    public List<DownloadEntry> Downloads { get; set; } = [];
    public List<CredentialEntry> Credentials { get; set; } = [];
    public List<string> LastSessionUrls { get; set; } = [];
}

public sealed record HistoryEntry(string Title, string Url, DateTimeOffset VisitedAt);

public sealed record BookmarkEntry(Guid Id, string Title, string Url, DateTimeOffset CreatedAt);

public sealed class DownloadEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string FileName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long ReceivedBytes { get; set; }
    public long TotalBytes { get; set; }
    public bool IsComplete { get; set; }
    public bool IsCancelled { get; set; }
    public bool IsInterrupted { get; set; }
    public string InterruptReason { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class CredentialEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Site { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}
