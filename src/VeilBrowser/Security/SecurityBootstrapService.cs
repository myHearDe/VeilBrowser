using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VeilBrowser.Core.Security;
using VeilBrowser.Infrastructure;

namespace VeilBrowser.Security;

public sealed class SecurityBootstrapService
{
    private static readonly byte[] DpapiEntropy =
        SHA256.HashData(Encoding.UTF8.GetBytes("VeilBrowser DPAPI key protection v1"));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly AppPaths _paths;

    public SecurityBootstrapService(AppPaths paths)
    {
        _paths = paths;
    }

    public bool IsConfigured => File.Exists(_paths.SecurityMetadata);

    public async Task<SecurityMetadata?> ReadMetadataAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.SecurityMetadata))
        {
            return null;
        }

        await using var stream = File.OpenRead(_paths.SecurityMetadata);
        return await JsonSerializer.DeserializeAsync<SecurityMetadata>(
            stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]> ConfigurePasswordAsync(
        string password,
        bool startupLock,
        CancellationToken cancellationToken = default)
    {
        PasswordKeyDeriver.ValidateNewPassword(password);
        var salt = PasswordKeyDeriver.CreateSalt();
        var unlockKey = await PasswordKeyDeriver.DeriveAsync(password, salt, cancellationToken)
            .ConfigureAwait(false);
        var masterKey = RandomNumberGenerator.GetBytes(32);
        var verifier = PasswordKeyDeriver.CreateVerifier(unlockKey);
        var wrappingKey = DataProtectionKeys.Derive(
            unlockKey,
            DataProtectionKeys.MasterKeyWrapContext);
        var wrappedMasterKey = AesGcmEnvelope.Encrypt(masterKey, wrappingKey);
        try
        {
            var metadata = new SecurityMetadata
            {
                Mode = KeyProtectionMode.MasterPassword,
                StartupLock = startupLock,
                SaltBase64 = Convert.ToBase64String(salt),
                VerifierBase64 = Convert.ToBase64String(verifier),
                WrappedMasterKeyBase64 = Convert.ToBase64String(wrappedMasterKey),
                DpapiProtectedKeyBase64 = startupLock ? string.Empty : ProtectForCurrentUser(masterKey)
            };

            await WriteMetadataAsync(metadata, cancellationToken).ConfigureAwait(false);
            return masterKey;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(masterKey);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(unlockKey);
            CryptographicOperations.ZeroMemory(wrappingKey);
            CryptographicOperations.ZeroMemory(verifier);
            CryptographicOperations.ZeroMemory(wrappedMasterKey);
            CryptographicOperations.ZeroMemory(salt);
        }
    }

    public async Task<byte[]> ConfigureWindowsAccountAsync(
        CancellationToken cancellationToken = default)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            var metadata = new SecurityMetadata
            {
                Mode = KeyProtectionMode.WindowsAccount,
                StartupLock = false,
                DpapiProtectedKeyBase64 = ProtectForCurrentUser(key)
            };
            await WriteMetadataAsync(metadata, cancellationToken).ConfigureAwait(false);
            return key;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(key);
            throw;
        }
    }

    public async Task<byte[]?> TryUnlockWithoutPasswordAsync(
        CancellationToken cancellationToken = default)
    {
        var metadata = await ReadMetadataAsync(cancellationToken).ConfigureAwait(false);
        if (metadata is null ||
            File.Exists(_paths.ForceLockMarker) ||
            metadata.StartupLock ||
            string.IsNullOrWhiteSpace(metadata.DpapiProtectedKeyBase64))
        {
            return null;
        }

        return UnprotectForCurrentUser(metadata.DpapiProtectedKeyBase64);
    }

    public async Task<byte[]?> UnlockWithPasswordAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        var metadata = await ReadMetadataAsync(cancellationToken).ConfigureAwait(false);
        if (metadata?.Mode != KeyProtectionMode.MasterPassword)
        {
            return null;
        }

        var salt = Convert.FromBase64String(metadata.SaltBase64);
        var expectedVerifier = Convert.FromBase64String(metadata.VerifierBase64);
        var unlockKey = await PasswordKeyDeriver.DeriveAsync(password, salt, cancellationToken)
            .ConfigureAwait(false);
        CryptographicOperations.ZeroMemory(salt);
        if (PasswordKeyDeriver.Verify(unlockKey, expectedVerifier))
        {
            CryptographicOperations.ZeroMemory(expectedVerifier);
            var wrappedMasterKey = Convert.FromBase64String(metadata.WrappedMasterKeyBase64);
            var wrappingKey = DataProtectionKeys.Derive(
                unlockKey,
                DataProtectionKeys.MasterKeyWrapContext);
            byte[] masterKey;
            try
            {
                try
                {
                    masterKey = AesGcmEnvelope.Decrypt(wrappedMasterKey, wrappingKey);
                }
                catch (CryptographicException)
                {
                    masterKey = AesGcmEnvelope.Decrypt(wrappedMasterKey, unlockKey);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(wrappedMasterKey);
                CryptographicOperations.ZeroMemory(wrappingKey);
                CryptographicOperations.ZeroMemory(unlockKey);
            }
            if (File.Exists(_paths.ForceLockMarker))
            {
                File.Delete(_paths.ForceLockMarker);
            }
            return masterKey;
        }

        CryptographicOperations.ZeroMemory(expectedVerifier);
        CryptographicOperations.ZeroMemory(unlockKey);
        return null;
    }

    public async Task<bool> VerifyPasswordAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        var key = await UnlockWithPasswordAsync(password, cancellationToken).ConfigureAwait(false);
        if (key is null)
        {
            return false;
        }

        CryptographicOperations.ZeroMemory(key);
        return true;
    }

    public async Task UpdateStartupLockAsync(
        bool startupLock,
        ReadOnlyMemory<byte> currentKey,
        CancellationToken cancellationToken = default)
    {
        var metadata = await ReadMetadataAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Security has not been configured.");
        if (metadata.Mode != KeyProtectionMode.MasterPassword)
        {
            return;
        }

        metadata.StartupLock = startupLock;
        metadata.DpapiProtectedKeyBase64 = startupLock
            ? string.Empty
            : ProtectForCurrentUser(currentKey.Span);
        await WriteMetadataAsync(metadata, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetOrChangeMasterPasswordAsync(
        string newPassword,
        bool startupLock,
        ReadOnlyMemory<byte> currentMasterKey,
        CancellationToken cancellationToken = default)
    {
        PasswordKeyDeriver.ValidateNewPassword(newPassword);
        var salt = PasswordKeyDeriver.CreateSalt();
        var unlockKey = await PasswordKeyDeriver.DeriveAsync(
            newPassword, salt, cancellationToken).ConfigureAwait(false);
        var verifier = PasswordKeyDeriver.CreateVerifier(unlockKey);
        var wrappingKey = DataProtectionKeys.Derive(
            unlockKey,
            DataProtectionKeys.MasterKeyWrapContext);
        var wrappedMasterKey = AesGcmEnvelope.Encrypt(currentMasterKey.Span, wrappingKey);
        try
        {
            var metadata = new SecurityMetadata
            {
                Mode = KeyProtectionMode.MasterPassword,
                StartupLock = startupLock,
                SaltBase64 = Convert.ToBase64String(salt),
                VerifierBase64 = Convert.ToBase64String(verifier),
                WrappedMasterKeyBase64 = Convert.ToBase64String(wrappedMasterKey),
                DpapiProtectedKeyBase64 = startupLock
                    ? string.Empty
                    : ProtectForCurrentUser(currentMasterKey.Span)
            };
            await WriteMetadataAsync(metadata, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(unlockKey);
            CryptographicOperations.ZeroMemory(wrappingKey);
            CryptographicOperations.ZeroMemory(verifier);
            CryptographicOperations.ZeroMemory(wrappedMasterKey);
        }
    }

    public async Task ForcePasswordPromptOnNextLaunchAsync(
        CancellationToken cancellationToken = default)
    {
        var metadata = await ReadMetadataAsync(cancellationToken).ConfigureAwait(false);
        if (metadata?.Mode != KeyProtectionMode.MasterPassword)
        {
            return;
        }

        await File.WriteAllTextAsync(
            _paths.ForceLockMarker, "locked", cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteMetadataAsync(
        SecurityMetadata metadata,
        CancellationToken cancellationToken)
    {
        var temporaryPath = _paths.SecurityMetadata + ".new";
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
                await JsonSerializer.SerializeAsync(
                    stream, metadata, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _paths.SecurityMetadata, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string ProtectForCurrentUser(ReadOnlySpan<byte> key)
    {
        var keyBytes = key.ToArray();
        byte[]? protectedBytes = null;
        try
        {
            protectedBytes = ProtectedData.Protect(
                keyBytes, DpapiEntropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            if (protectedBytes is not null)
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
            }
        }
    }

    private static byte[] UnprotectForCurrentUser(string protectedKeyBase64)
    {
        var protectedBytes = Convert.FromBase64String(protectedKeyBase64);
        try
        {
            return ProtectedData.Unprotect(
                protectedBytes, DpapiEntropy, DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }
}
