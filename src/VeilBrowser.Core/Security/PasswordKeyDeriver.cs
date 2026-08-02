using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace VeilBrowser.Core.Security;

public static class PasswordKeyDeriver
{
    public const int SaltSize = 16;
    public const int KeySize = 32;
    public const int MinimumPasswordLength = 12;

    public static byte[] CreateSalt() => RandomNumberGenerator.GetBytes(SaltSize);

    public static async Task<byte[]> DeriveAsync(
        string password,
        ReadOnlyMemory<byte> salt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (salt.Length < SaltSize)
        {
            throw new ArgumentException("Salt must contain at least 16 bytes.", nameof(salt));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var saltBytes = salt.ToArray();
        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = saltBytes,
                DegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
                Iterations = 4,
                MemorySize = 64 * 1024
            };

            var key = await argon2.GetBytesAsync(KeySize).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return key;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(saltBytes);
        }
    }

    public static byte[] CreateVerifier(ReadOnlySpan<byte> key)
    {
        ReadOnlySpan<byte> context = "VeilBrowser master key verifier v1"u8;
        return HMACSHA256.HashData(key, context);
    }

    public static void ValidateNewPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (password.Length < MinimumPasswordLength)
        {
            throw new ArgumentException(
                $"Master password must contain at least {MinimumPasswordLength} characters.",
                nameof(password));
        }

        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            throw new ArgumentException(
                "Master password must contain at least one letter and one number.",
                nameof(password));
        }
    }

    public static bool Verify(ReadOnlySpan<byte> key, ReadOnlySpan<byte> expectedVerifier)
    {
        var actual = CreateVerifier(key);
        try
        {
            return CryptographicOperations.FixedTimeEquals(actual, expectedVerifier);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actual);
        }
    }
}
