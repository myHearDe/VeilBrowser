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

    public EncryptedJsonStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
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
        try
        {
            plaintext = AesGcmEnvelope.Decrypt(envelope, key.Span);
            return JsonSerializer.Deserialize<T>(plaintext, JsonOptions) ?? new T();
        }
        finally
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
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
        var envelope = AesGcmEnvelope.Encrypt(plaintext, key.Span);
        var temporaryPath = _path + ".new";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, envelope, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(envelope);
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
