using System.Security.Cryptography;
using System.Text;
using VeilBrowser.Core.Models;
using VeilBrowser.Core.Security;

var key = RandomNumberGenerator.GetBytes(32);
var plaintext = Encoding.UTF8.GetBytes("隐栈浏览器 encryption smoke test");

var envelope = AesGcmEnvelope.Encrypt(plaintext, key);
var decrypted = AesGcmEnvelope.Decrypt(envelope, key);
Require(plaintext.SequenceEqual(decrypted), "AES-GCM envelope round-trip failed.");

await using var chunkInput = new MemoryStream(RandomNumberGenerator.GetBytes(3_000_123));
await using var encrypted = new MemoryStream();
await ChunkedAesGcmFile.EncryptAsync(chunkInput, encrypted, key);
encrypted.Position = 0;
await using var chunkOutput = new MemoryStream();
await ChunkedAesGcmFile.DecryptAsync(encrypted, chunkOutput, key);
Require(chunkInput.ToArray().SequenceEqual(chunkOutput.ToArray()), "Chunked AES-GCM round-trip failed.");

var encryptedBytes = encrypted.ToArray();
var truncatedAtChunkBoundary = encryptedBytes
    .AsSpan(0, 8 + 1 + sizeof(int) + 12 + sizeof(int) + ChunkedAesGcmFile.DefaultChunkSize + 16)
    .ToArray();
Array.Resize(ref truncatedAtChunkBoundary, truncatedAtChunkBoundary.Length + sizeof(int));
await RequireThrowsAsync(
    () => ChunkedAesGcmFile.DecryptAsync(
        new MemoryStream(truncatedAtChunkBoundary),
        new MemoryStream(),
        key),
    "Truncated chunked container was accepted.");

var containerWithTrailingData = new byte[encryptedBytes.Length + 1];
encryptedBytes.CopyTo(containerWithTrailingData, 0);
containerWithTrailingData[^1] = 0x7F;
await RequireThrowsAsync(
    () => ChunkedAesGcmFile.DecryptAsync(
        new MemoryStream(containerWithTrailingData),
        new MemoryStream(),
        key),
    "Chunked container with trailing data was accepted.");

var salt = PasswordKeyDeriver.CreateSalt();
var derived = await PasswordKeyDeriver.DeriveAsync("correct horse battery staple", salt);
var verifier = PasswordKeyDeriver.CreateVerifier(derived);
Require(PasswordKeyDeriver.Verify(derived, verifier), "Argon2id verifier failed.");

var storePath = Path.Combine(Path.GetTempPath(), $"veil-store-{Guid.NewGuid():N}.enc");
try
{
    var store = new EncryptedJsonStore<BrowserState>(storePath);
    var state = new BrowserState();
    state.Bookmarks.Add(new BookmarkEntry(Guid.NewGuid(), "Example", "https://example.com", DateTimeOffset.Now));
    await store.SaveAsync(state, key);
    var restored = await store.LoadAsync(key);
    Require(restored.Bookmarks.Count == 1 && restored.Bookmarks[0].Url == "https://example.com",
        "Encrypted JSON store round-trip failed.");
}
finally
{
    if (File.Exists(storePath))
    {
        File.Delete(storePath);
    }
}

var profileTestRoot = Path.Combine(
    Path.GetTempPath(),
    $"veil-profile-test-{Guid.NewGuid():N}");
var workingProfilePath = Path.Combine(profileTestRoot, "working-profile");
var profileContainerPath = Path.Combine(profileTestRoot, "profile.veil");
var lockedProfileFile = Path.Combine(workingProfilePath, "WebView2", "locked-cache.bin");
var expectedProfileData = RandomNumberGenerator.GetBytes(2_000_123);
try
{
    Directory.CreateDirectory(Path.GetDirectoryName(lockedProfileFile)!);
    await File.WriteAllBytesAsync(lockedProfileFile, expectedProfileData);
    await File.WriteAllTextAsync(
        Path.Combine(workingProfilePath, "Preferences"),
        """{"browser":"WebView2","language":"zh-CN"}""");

    var temporaryLock = new FileStream(
        lockedProfileFile,
        FileMode.Open,
        FileAccess.ReadWrite,
        FileShare.None);
    var releaseLock = Task.Run(async () =>
    {
        await Task.Delay(550);
        await temporaryLock.DisposeAsync();
    });

    await ProfileContainerService.ProtectAsync(
        workingProfilePath,
        profileContainerPath,
        key);
    await releaseLock;
    Require(
        File.Exists(profileContainerPath) && !Directory.Exists(workingProfilePath),
        "Profile protection did not atomically replace the working directory.");

    await ProfileContainerService.RestoreAsync(
        profileContainerPath,
        workingProfilePath,
        key);
    var restoredProfileData = await File.ReadAllBytesAsync(lockedProfileFile);
    Require(
        expectedProfileData.SequenceEqual(restoredProfileData),
        "Encrypted profile container round-trip failed.");
}
finally
{
    if (Directory.Exists(profileTestRoot))
    {
        Directory.Delete(profileTestRoot, recursive: true);
    }
}

CryptographicOperations.ZeroMemory(key);
CryptographicOperations.ZeroMemory(derived);
CryptographicOperations.ZeroMemory(expectedProfileData);
Console.WriteLine("All VeilBrowser core smoke tests passed.");
return;

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static async Task RequireThrowsAsync(Func<Task> action, string message)
{
    try
    {
        await action();
    }
    catch (Exception ex) when (ex is CryptographicException or EndOfStreamException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}
