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
        entry.FileName = GetSafeFileName(e.ResultFilePath, operation.Uri);
        UpdateEntry(operation, entry);

        operation.BytesReceivedChanged += Operation_BytesReceivedChanged;
        operation.StateChanged += Operation_StateChanged;

        // Keep Handled=false so WebView2 shows its normal save/download UI.
        _onUpdated(entry);
        return;

        void Operation_BytesReceivedChanged(object? _, object __)
        {
            UpdateEntry(operation, entry);
            _onUpdated(entry);
        }

        void Operation_StateChanged(object? _, object __)
        {
            UpdateEntry(operation, entry);
            _onUpdated(entry);
            if (operation.State is CoreWebView2DownloadState.Completed or
                CoreWebView2DownloadState.Interrupted)
            {
                operation.BytesReceivedChanged -= Operation_BytesReceivedChanged;
                operation.StateChanged -= Operation_StateChanged;
                _downloads.Remove(operation);
            }
        }
    }

    private DownloadEntry GetOrCreate(CoreWebView2DownloadOperation operation)
    {
        if (_downloads.TryGetValue(operation, out var existing))
        {
            return existing;
        }

        var created = new DownloadEntry
        {
            FileName = GetSafeFileName(operation.ResultFilePath, operation.Uri),
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
        entry.FileName = GetSafeFileName(operation.ResultFilePath, operation.Uri);
        entry.TotalBytes = ToInt64(operation.TotalBytesToReceive ?? 0);
        entry.ReceivedBytes = operation.BytesReceived;
        entry.IsComplete = operation.State == CoreWebView2DownloadState.Completed;
        entry.IsInterrupted = operation.State == CoreWebView2DownloadState.Interrupted;
        entry.IsCancelled =
            entry.IsInterrupted &&
            operation.InterruptReason == CoreWebView2DownloadInterruptReason.UserCanceled;
        entry.InterruptReason = entry.IsInterrupted
            ? operation.InterruptReason.ToString()
            : string.Empty;
    }

    private static string GetSafeFileName(string? resultPath, string? uri)
    {
        try
        {
            var fileName = Path.GetFileName(resultPath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return fileName;
            }
        }
        catch (ArgumentException)
        {
            // Fall back to the URL or a neutral name for malformed paths.
        }

        return Uri.TryCreate(uri, UriKind.Absolute, out var parsed) &&
               !string.IsNullOrWhiteSpace(Path.GetFileName(parsed.LocalPath))
            ? Path.GetFileName(parsed.LocalPath)
            : "download";
    }

    private static long ToInt64(ulong value) =>
        value > long.MaxValue ? long.MaxValue : (long)value;
}
