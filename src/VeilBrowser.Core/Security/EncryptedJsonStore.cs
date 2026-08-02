using System.Security.Cryptography;
using System.IO;
using System.Text.Json;

namespace VeilBrowser.Core.Security;

public sealed class EncryptedJsonStore<T> where T : class, new()
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _path;
    private readonly string? _keyContext;

    public EncryptedJsonStore(string path, string? keyContext = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _keyContext = keyContext;
    }

    public async Task<T> LoadAsync(
        ReadOnlyMemory<byte> key,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return new T();
        }

        var envelope = await File.ReadAllBytesAsync(_path, cancellationToken).ConfigureAwait(false);
        byte[]? plaintext = null;
        byte[]? derivedKey = null;
        try
        {
            if (_keyContext is null)
            {
                plaintext = AesGcmEnvelope.Decrypt(envelope, key.Span);
            }
            else
            {
                derivedKey = DataProtectionKeys.Derive(key.Span, _keyContext);
                try
                {
                    plaintext = AesGcmEnvelope.Decrypt(envelope, derivedKey);
                }
                catch (CryptographicException)
                {
                    // Compatibility path for profiles written before subkey
                    // separation. The next save transparently migrates them.
                    plaintext = AesGcmEnvelope.Decrypt(envelope, key.Span);
                }
            }
            return JsonSerializer.Deserialize<T>(plaintext, JsonOptions) ?? new T();
        }
        finally
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
            if (derivedKey is not null)
            {
                CryptographicOperations.ZeroMemory(derivedKey);
            }
            CryptographicOperations.ZeroMemory(envelope);
        }
    }

    public async Task SaveAsync(
        T value,
        ReadOnlyMemory<byte> key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        byte[]? derivedKey = null;
        var encryptionKey = key.Span;
        if (_keyContext is not null)
        {
            derivedKey = DataProtectionKeys.Derive(key.Span, _keyContext);
            encryptionKey = derivedKey;
        }
        var envelope = AesGcmEnvelope.Encrypt(plaintext, encryptionKey);
        var temporaryPath = _path + ".new";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(envelope, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(envelope);
            if (derivedKey is not null)
            {
                CryptographicOperations.ZeroMemory(derivedKey);
            }
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
