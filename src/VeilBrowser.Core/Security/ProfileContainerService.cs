using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

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
            // A previous shutdown may have failed after WebView2 released only
            // part of its profile. Keep that recoverable plaintext copy rather
            // than replacing it with an older encrypted snapshot.
            if (Directory.EnumerateFileSystemEntries(workingProfilePath).Any())
            {
                return;
            }

            Directory.Delete(workingProfilePath, recursive: true);
        }

        Directory.CreateDirectory(workingProfilePath);
        if (!File.Exists(encryptedContainerPath))
        {
            return;
        }

        var zipPath = CreatePrivateTemporaryPath(workingProfilePath, "veil-restore", ".zip");
        var profileKey = DataProtectionKeys.Derive(
            key.Span,
            DataProtectionKeys.ProfileContainerContext);
        try
        {
            try
            {
                await DecryptContainerAsync(
                    encryptedContainerPath,
                    zipPath,
                    profileKey,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is CryptographicException or EndOfStreamException)
            {
                SecureDeleteBestEffort(zipPath);
                try
                {
                    await DecryptContainerAsync(
                        encryptedContainerPath,
                        zipPath,
                        key,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception legacyKeyError) when (
                    legacyKeyError is CryptographicException or EndOfStreamException)
                {
                    SecureDeleteBestEffort(zipPath);
                    await DecryptLegacyContainerAsync(
                        encryptedContainerPath,
                        zipPath,
                        key,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            ExtractArchiveSafely(zipPath, workingProfilePath);
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
            CryptographicOperations.ZeroMemory(profileKey);
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

        var zipPath = CreatePrivateTemporaryPath(encryptedContainerPath, "veil-protect", ".zip");
        var newContainerPath = encryptedContainerPath + ".new";
        var profileKey = DataProtectionKeys.Derive(
            key.Span,
            DataProtectionKeys.ProfileContainerContext);
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
                await ChunkedAesGcmFile.EncryptAsync(
                    input,
                    output,
                    profileKey,
                    cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(newContainerPath, encryptedContainerPath, overwrite: true);
            Directory.Delete(workingProfilePath, recursive: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(profileKey);
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
        for (var attempt = 0; attempt < 12; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SecureDeleteBestEffort(zipPath);
            try
            {
                await CreateArchiveAsync(sourceDirectory, zipPath, cancellationToken)
                    .ConfigureAwait(false);
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

    private static async Task CreateArchiveAsync(
        string sourceDirectory,
        string zipPath,
        CancellationToken cancellationToken)
    {
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false
        };

        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var filePath in Directory.EnumerateFiles(
                     sourceDirectory,
                     "*",
                     enumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsVolatileWebViewFile(filePath))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(sourceDirectory, filePath)
                .Replace(Path.DirectorySeparatorChar, '/');
            var entry = archive.CreateEntry(relativePath, CompressionLevel.Fastest);
            await using var input = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = entry.Open();
            await input.CopyToAsync(output, 1024 * 1024, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static bool IsVolatileWebViewFile(string filePath)
    {
        var name = Path.GetFileName(filePath);
        return name.Equals("SingletonLock", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("SingletonCookie", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("SingletonSocket", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("LOCK", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("lockfile", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreatePrivateTemporaryPath(
        string anchorPath,
        string prefix,
        string extension)
    {
        var directory = Path.GetDirectoryName(anchorPath) ?? Path.GetTempPath();
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $".{prefix}-{Guid.NewGuid():N}{extension}");
    }

    private static async Task DecryptContainerAsync(
        string containerPath,
        string outputPath,
        ReadOnlyMemory<byte> key,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            containerPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            outputPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await ChunkedAesGcmFile.DecryptAsync(input, output, key, cancellationToken)
            .ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task DecryptLegacyContainerAsync(
        string containerPath,
        string outputPath,
        ReadOnlyMemory<byte> key,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            containerPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            outputPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await ChunkedAesGcmFile.DecryptLegacyAsync(
            input,
            output,
            key,
            cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ExtractArchiveSafely(string zipPath, string destinationDirectory)
    {
        var destinationRoot = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(destinationRoot);
        if (!destinationRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            destinationRoot += Path.DirectorySeparatorChar;
        }

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.FullName))
            {
                continue;
            }

            var entryPath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(entryPath) ||
                entryPath.Split(Path.DirectorySeparatorChar)
                    .Any(part => part == ".."))
            {
                throw new IOException($"Encrypted profile archive contains an unsafe path: {entry.FullName}");
            }

            var fullPath = Path.GetFullPath(Path.Combine(destinationRoot, entryPath));
            if (!fullPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"Encrypted profile archive escapes the profile directory: {entry.FullName}");
            }

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(fullPath);
                continue;
            }

            var parent = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            entry.ExtractToFile(fullPath, overwrite: true);
        }
    }
}
