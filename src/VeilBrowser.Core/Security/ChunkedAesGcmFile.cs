using System.Buffers.Binary;
using System.Security.Cryptography;

namespace VeilBrowser.Core.Security;

public static class ChunkedAesGcmFile
{
    private static ReadOnlySpan<byte> Magic => "VEILPF01"u8;
    private const byte LegacyVersion = 1;
    private const byte CurrentVersion = 2;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    public const int DefaultChunkSize = 1024 * 1024;

    public static async Task EncryptAsync(
        Stream input,
        Stream output,
        ReadOnlyMemory<byte> key,
        int chunkSize = DefaultChunkSize,
        CancellationToken cancellationToken = default)
    {
        Validate(input, output, key.Span, chunkSize);
        var baseNonce = RandomNumberGenerator.GetBytes(NonceSize);
        var header = new byte[8 + 1 + sizeof(int) + NonceSize];
        Magic.CopyTo(header);
        header[8] = CurrentVersion;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(9, sizeof(int)), chunkSize);
        baseNonce.CopyTo(header, 9 + sizeof(int));
        await output.WriteAsync(header, cancellationToken).ConfigureAwait(false);

        var plaintext = GC.AllocateUninitializedArray<byte>(chunkSize);
        var ciphertext = GC.AllocateUninitializedArray<byte>(chunkSize);
        var tag = new byte[TagSize];
        var nonce = new byte[NonceSize];
        var recordLength = new byte[sizeof(int)];
        var aad = new byte[12];
        Magic.CopyTo(aad);

        try
        {
            using var aes = new AesGcm(key.Span, TagSize);
            uint counter = 0;
            while (true)
            {
                var read = await ReadUpToAsync(input, plaintext, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(recordLength, 0);
                    await output.WriteAsync(recordLength, cancellationToken).ConfigureAwait(false);
                    CreateNonce(baseNonce, counter, nonce);
                    BinaryPrimitives.WriteUInt32LittleEndian(aad.AsSpan(8), counter);
                    aes.Encrypt(
                        nonce,
                        ReadOnlySpan<byte>.Empty,
                        Span<byte>.Empty,
                        tag,
                        aad);
                    await output.WriteAsync(tag, cancellationToken).ConfigureAwait(false);
                    break;
                }

                CreateNonce(baseNonce, counter, nonce);
                BinaryPrimitives.WriteUInt32LittleEndian(aad.AsSpan(8), counter);
                aes.Encrypt(
                    nonce,
                    plaintext.AsSpan(0, read),
                    ciphertext.AsSpan(0, read),
                    tag,
                    aad);

                BinaryPrimitives.WriteInt32LittleEndian(recordLength, read);
                await output.WriteAsync(recordLength, cancellationToken).ConfigureAwait(false);
                await output.WriteAsync(ciphertext.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                await output.WriteAsync(tag, cancellationToken).ConfigureAwait(false);
                checked { counter++; }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(nonce);
        }
    }

    public static async Task DecryptAsync(
        Stream input,
        Stream output,
        ReadOnlyMemory<byte> key,
        CancellationToken cancellationToken = default)
    {
        await DecryptCoreAsync(
            input,
            output,
            key,
            allowLegacyVersion: false,
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task DecryptLegacyAsync(
        Stream input,
        Stream output,
        ReadOnlyMemory<byte> key,
        CancellationToken cancellationToken = default)
    {
        await DecryptCoreAsync(
            input,
            output,
            key,
            allowLegacyVersion: true,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task DecryptCoreAsync(
        Stream input,
        Stream output,
        ReadOnlyMemory<byte> key,
        bool allowLegacyVersion,
        CancellationToken cancellationToken)
    {
        var header = new byte[8 + 1 + sizeof(int) + NonceSize];
        await ReadExactlyAsync(input, header, cancellationToken).ConfigureAwait(false);
        var version = header[8];
        // Version 1 had no authenticated end-of-file record and could be
        // truncated at a chunk boundary without detection. Public callers do
        // not accept it; ProfileContainerService has a constrained one-time
        // migration path whose output must also be a structurally valid ZIP.
        if (!header.AsSpan(0, 8).SequenceEqual(Magic) ||
            (version != CurrentVersion &&
             !(allowLegacyVersion && version == LegacyVersion)))
        {
            throw new CryptographicException("Invalid profile container.");
        }

        var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(9, sizeof(int)));
        Validate(input, output, key.Span, chunkSize);
        var baseNonce = header.AsSpan(9 + sizeof(int), NonceSize).ToArray();
        var ciphertext = GC.AllocateUninitializedArray<byte>(chunkSize);
        var plaintext = GC.AllocateUninitializedArray<byte>(chunkSize);
        var tag = new byte[TagSize];
        var nonce = new byte[NonceSize];
        var recordLength = new byte[sizeof(int)];
        var aad = new byte[12];
        Magic.CopyTo(aad);

        try
        {
            using var aes = new AesGcm(key.Span, TagSize);
            uint counter = 0;
            while (true)
            {
                await ReadExactlyAsync(input, recordLength, cancellationToken).ConfigureAwait(false);
                var length = BinaryPrimitives.ReadInt32LittleEndian(recordLength);
                if (length == 0)
                {
                    if (version == CurrentVersion)
                    {
                        await ReadExactlyAsync(input, tag, cancellationToken).ConfigureAwait(false);
                        CreateNonce(baseNonce, counter, nonce);
                        BinaryPrimitives.WriteUInt32LittleEndian(aad.AsSpan(8), counter);
                        aes.Decrypt(
                            nonce,
                            ReadOnlySpan<byte>.Empty,
                            tag,
                            Span<byte>.Empty,
                            aad);
                    }

                    await EnsureEndOfStreamAsync(input, cancellationToken).ConfigureAwait(false);
                    break;
                }

                if (length < 0 || length > chunkSize)
                {
                    throw new CryptographicException("Invalid encrypted chunk length.");
                }

                await ReadExactlyAsync(input, ciphertext.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
                await ReadExactlyAsync(input, tag, cancellationToken).ConfigureAwait(false);
                CreateNonce(baseNonce, counter, nonce);
                BinaryPrimitives.WriteUInt32LittleEndian(aad.AsSpan(8), counter);
                aes.Decrypt(
                    nonce,
                    ciphertext.AsSpan(0, length),
                    tag,
                    plaintext.AsSpan(0, length),
                    aad);
                await output.WriteAsync(plaintext.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
                checked { counter++; }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(nonce);
        }
    }

    private static void Validate(Stream input, Stream output, ReadOnlySpan<byte> key, int chunkSize)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        if (!input.CanRead || !output.CanWrite)
        {
            throw new ArgumentException("Input must be readable and output must be writable.");
        }

        if (key.Length is not (16 or 24 or 32))
        {
            throw new ArgumentException("AES key must be 16, 24, or 32 bytes.", nameof(key));
        }

        if (chunkSize is < 4096 or > 16 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize));
        }
    }

    private static void CreateNonce(ReadOnlySpan<byte> baseNonce, uint counter, Span<byte> nonce)
    {
        baseNonce.CopyTo(nonce);
        var seed = BinaryPrimitives.ReadUInt32BigEndian(nonce[8..]);
        BinaryPrimitives.WriteUInt32BigEndian(nonce[8..], seed ^ counter);
    }

    private static async Task<int> ReadUpToAsync(
        Stream input,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await input.ReadAsync(buffer[total..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static async Task ReadExactlyAsync(
        Stream input,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await input.ReadAsync(buffer[total..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Encrypted container ended unexpectedly.");
            }

            total += read;
        }
    }

    private static async Task EnsureEndOfStreamAsync(
        Stream input,
        CancellationToken cancellationToken)
    {
        var trailingByte = new byte[1];
        if (await input.ReadAsync(trailingByte, cancellationToken).ConfigureAwait(false) != 0)
        {
            throw new CryptographicException("Encrypted container has trailing data.");
        }
    }
}
