using System.IO;
using System.IO.Compression;

namespace VeilBrowser.Core.Security;

public sealed class ProfileContainerService
{
    public static async Task RestoreAsync(
        string encryptedContainerPath,
        string workingProfilePath,
        ReadOnlyMemory<byte> key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedContainerPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingProfilePath);

        if (Directory.Exists(workingProfilePath))
        {
            Directory.Delete(workingProfilePath, recursive: true);
        }

        Directory.CreateDirectory(workingProfilePath);
        if (!File.Exists(encryptedContainerPath))
        {
            return;
        }

        var zipPath = Path.Combine(Path.GetTempPath(), $"veil-restore-{Guid.NewGuid():N}.zip");
        try
        {
            await using (var input = new FileStream(
                encryptedContainerPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(
                zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await ChunkedAesGcmFile.DecryptAsync(input, output, key, cancellationToken)
                    .ConfigureAwait(false);
            }

            ZipFile.ExtractToDirectory(zipPath, workingProfilePath, overwriteFiles: true);
        }
        catch
        {
            if (Directory.Exists(workingProfilePath))
            {
                Directory.Delete(workingProfilePath, recursive: true);
            }

            throw;
        }
        finally
        {
            SecureDeleteBestEffort(zipPath);
        }
    }

    public static async Task ProtectAsync(
        string workingProfilePath,
        string encryptedContainerPath,
        ReadOnlyMemory<byte> key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingProfilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedContainerPath);
        if (!Directory.Exists(workingProfilePath))
        {
            return;
        }

        var containerDirectory = Path.GetDirectoryName(encryptedContainerPath);
        if (!string.IsNullOrEmpty(containerDirectory))
        {
            Directory.CreateDirectory(containerDirectory);
        }

        var zipPath = Path.Combine(Path.GetTempPath(), $"veil-protect-{Guid.NewGuid():N}.zip");
        var newContainerPath = encryptedContainerPath + ".new";
        try
        {
            await CreateArchiveWithRetryAsync(
                workingProfilePath,
                zipPath,
                cancellationToken).ConfigureAwait(false);

            await using (var input = new FileStream(
                zipPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(
                newContainerPath, FileMode.Create, FileAccess.Write, FileShare.None,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await ChunkedAesGcmFile.EncryptAsync(input, output, key, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(newContainerPath, encryptedContainerPath, overwrite: true);
            Directory.Delete(workingProfilePath, recursive: true);
        }
        finally
        {
            SecureDeleteBestEffort(zipPath);
            if (File.Exists(newContainerPath))
            {
                File.Delete(newContainerPath);
            }
        }
    }

    public static void SecureDeleteBestEffort(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var length = new FileInfo(path).Length;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
            var zeroes = new byte[1024 * 1024];
            long written = 0;
            while (written < length)
            {
                var count = (int)Math.Min(zeroes.Length, length - written);
                stream.Write(zeroes, 0, count);
                written += count;
            }

            stream.Flush(flushToDisk: true);
        }
        catch (IOException)
        {
            // Best effort only: SSD wear levelling can prevent guaranteed physical erasure.
        }
        catch (UnauthorizedAccessException)
        {
            // The caller still receives a normal delete attempt below.
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Best effort only.
            }
            catch (UnauthorizedAccessException)
            {
                // Best effort only.
            }
        }
    }

    private static async Task CreateArchiveWithRetryAsync(
        string sourceDirectory,
        string zipPath,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SecureDeleteBestEffort(zipPath);
            try
            {
                ZipFile.CreateFromDirectory(
                    sourceDirectory,
                    zipPath,
                    CompressionLevel.Fastest,
                    includeBaseDirectory: false);
                return;
            }
            catch (IOException ex)
            {
                lastError = ex;
            }
            catch (UnauthorizedAccessException ex)
            {
                lastError = ex;
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(200 * (attempt + 1)),
                cancellationToken).ConfigureAwait(false);
        }

        throw new IOException(
            "浏览器内核仍在释放配置文件，无法创建加密快照。",
            lastError);
    }
}
