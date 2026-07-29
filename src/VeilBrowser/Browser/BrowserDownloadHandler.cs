using System.IO;
using Microsoft.Web.WebView2.Core;
using VeilBrowser.Core.Models;

namespace VeilBrowser.Browser;

public sealed class BrowserDownloadHandler
{
    private readonly Action<DownloadEntry> _onUpdated;
    private readonly Dictionary<CoreWebView2DownloadOperation, DownloadEntry> _downloads = [];

    public BrowserDownloadHandler(Action<DownloadEntry> onUpdated)
    {
        _onUpdated = onUpdated;
    }

    public void HandleDownloadStarting(
        object? sender,
        CoreWebView2DownloadStartingEventArgs e)
    {
        var operation = e.DownloadOperation;
        var entry = GetOrCreate(operation);
        entry.FullPath = e.ResultFilePath;
        entry.FileName = Path.GetFileName(e.ResultFilePath);
        UpdateEntry(operation, entry);

        operation.BytesReceivedChanged += (_, _) =>
        {
            UpdateEntry(operation, entry);
            _onUpdated(entry);
        };
        operation.StateChanged += (_, _) =>
        {
            UpdateEntry(operation, entry);
            _onUpdated(entry);
        };

        // Keep Handled=false so WebView2 shows its normal save/download UI.
        _onUpdated(entry);
    }

    private DownloadEntry GetOrCreate(CoreWebView2DownloadOperation operation)
    {
        if (_downloads.TryGetValue(operation, out var existing))
        {
            return existing;
        }

        var created = new DownloadEntry
        {
            FileName = Path.GetFileName(operation.ResultFilePath),
            Url = operation.Uri,
            FullPath = operation.ResultFilePath,
            TotalBytes = ToInt64(operation.TotalBytesToReceive ?? 0),
            ReceivedBytes = operation.BytesReceived
        };
        _downloads[operation] = created;
        return created;
    }

    private static void UpdateEntry(
        CoreWebView2DownloadOperation operation,
        DownloadEntry entry)
    {
        entry.FullPath = operation.ResultFilePath;
        entry.FileName = Path.GetFileName(operation.ResultFilePath);
        entry.TotalBytes = ToInt64(operation.TotalBytesToReceive ?? 0);
        entry.ReceivedBytes = operation.BytesReceived;
        entry.IsComplete = operation.State == CoreWebView2DownloadState.Completed;
        entry.IsCancelled =
            operation.State == CoreWebView2DownloadState.Interrupted &&
            operation.InterruptReason == CoreWebView2DownloadInterruptReason.UserCanceled;
    }

    private static long ToInt64(ulong value) =>
        value > long.MaxValue ? long.MaxValue : (long)value;
}
