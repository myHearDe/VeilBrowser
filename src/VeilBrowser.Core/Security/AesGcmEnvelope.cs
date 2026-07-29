using System.Buffers.Binary;
using System.Security.Cryptography;

namespace VeilBrowser.Core.Security;

public static class AesGcmEnvelope
{
    private static ReadOnlySpan<byte> Magic => "VEILENC1"u8;
    private const byte Version = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 8 + 1 + NonceSize + TagSize + sizeof(int);

    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key)
    {
        ValidateKey(key);
        var result = GC.AllocateUninitializedArray<byte>(HeaderSize + plaintext.Length);
        Magic.CopyTo(result);
        result[8] = Version;

        var nonce = result.AsSpan(9, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        var tag = result.AsSpan(9 + NonceSize, TagSize);
        BinaryPrimitives.WriteInt32LittleEndian(
            result.AsSpan(9 + NonceSize + TagSize, sizeof(int)),
            plaintext.Length);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(
            nonce,
            plaintext,
            result.AsSpan(HeaderSize),
            tag,
            Magic);
        return result;
    }

    public static byte[] Decrypt(ReadOnlySpan<byte> envelope, ReadOnlySpan<byte> key)
    {
        ValidateKey(key);
        if (envelope.Length < HeaderSize ||
            !envelope[..8].SequenceEqual(Magic) ||
            envelope[8] != Version)
        {
            throw new CryptographicException("Invalid encrypted envelope.");
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(
            envelope.Slice(9 + NonceSize + TagSize, sizeof(int)));
        if (length < 0 || envelope.Length != HeaderSize + length)
        {
            throw new CryptographicException("Encrypted envelope length is invalid.");
        }

        var plaintext = GC.AllocateUninitializedArray<byte>(length);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(
                envelope.Slice(9, NonceSize),
                envelope.Slice(HeaderSize),
                envelope.Slice(9 + NonceSize, TagSize),
                plaintext,
                Magic);
            return plaintext;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
    }

    private static void ValidateKey(ReadOnlySpan<byte> key)
    {
        if (key.Length is not (16 or 24 or 32))
        {
            throw new ArgumentException("AES key must be 16, 24, or 32 bytes.", nameof(key));
        }
    }
}
