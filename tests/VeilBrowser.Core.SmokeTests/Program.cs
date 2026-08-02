using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;
using VeilBrowser.Core.Models;
using VeilBrowser.Core.Security;

var key = RandomNumberGenerator.GetBytes(32);
var plaintext = Encoding.UTF8.GetBytes("隐栈浏览器 encryption smoke test");

Require(
    new SecurityPreferences().HomePage ==
    "https://veil.local/index.html?theme=midnight",
    "Default home page must use the built-in midnight page.");

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
PasswordKeyDeriver.ValidateNewPassword("secure password 2026");
RequireThrows<ArgumentException>(
    () => PasswordKeyDeriver.ValidateNewPassword("short1"),
    "A short master password was accepted.");
RequireThrows<ArgumentException>(
    () => PasswordKeyDeriver.ValidateNewPassword("letters only password"),
    "A master password without a number was accepted.");

var stateKey = DataProtectionKeys.Derive(key, DataProtectionKeys.BrowserStateContext);
var profileKey = DataProtectionKeys.Derive(key, DataProtectionKeys.ProfileContainerContext);
var wrapKey = DataProtectionKeys.Derive(key, DataProtectionKeys.MasterKeyWrapContext);
Require(!stateKey.SequenceEqual(profileKey), "State and profile subkeys must differ.");
Require(!stateKey.SequenceEqual(wrapKey), "State and wrapping subkeys must differ.");
Require(!profileKey.SequenceEqual(wrapKey), "Profile and wrapping subkeys must differ.");

var storePath = Path.Combine(Path.GetTempPath(), $"veil-store-{Guid.NewGuid():N}.enc");
try
{
    var store = new EncryptedJsonStore<BrowserState>(storePath);
    var state = new BrowserState();
    state.Preferences.Theme = BrowserTheme.GraphiteFocus;
    state.Bookmarks.Add(new BookmarkEntry(Guid.NewGuid(), "Example", "https://example.com", DateTimeOffset.Now));
    await store.SaveAsync(state, key);
    var restored = await store.LoadAsync(key);
    Require(restored.Bookmarks.Count == 1 && restored.Bookmarks[0].Url == "https://example.com",
        "Encrypted JSON store round-trip failed.");
    Require(restored.Preferences.Theme == BrowserTheme.GraphiteFocus,
        "Browser theme preference round-trip failed.");
}
finally
{
    if (File.Exists(storePath))
    {
        File.Delete(storePath);
    }
}

var legacyStorePath = Path.Combine(Path.GetTempPath(), $"veil-legacy-store-{Guid.NewGuid():N}.enc");
try
{
    var legacyStore = new EncryptedJsonStore<BrowserState>(legacyStorePath);
    var legacyState = new BrowserState();
    legacyState.Bookmarks.Add(new BookmarkEntry(
        Guid.NewGuid(), "Legacy", "https://legacy.example", DateTimeOffset.Now));
    await legacyStore.SaveAsync(legacyState, key);

    var migratedStore = new EncryptedJsonStore<BrowserState>(
        legacyStorePath,
        DataProtectionKeys.BrowserStateContext);
    var migratedState = await migratedStore.LoadAsync(key);
    Require(migratedState.Bookmarks.Single().Url == "https://legacy.example",
        "Legacy encrypted state compatibility load failed.");
    await migratedStore.SaveAsync(migratedState, key);

    await RequireThrowsAsync(
        () => legacyStore.LoadAsync(key),
        "Migrated state was still decryptable with the undiversified master key.");
}
finally
{
    if (File.Exists(legacyStorePath))
    {
        File.Delete(legacyStorePath);
    }
}

var legacyVersionContainer = encryptedBytes.ToArray();
legacyVersionContainer[8] = 1;
await RequireThrowsAsync(
    () => ChunkedAesGcmFile.DecryptAsync(
        new MemoryStream(legacyVersionContainer),
        new MemoryStream(),
        key),
    "Unauthenticated version 1 profile container was accepted.");

var legacyProfileRoot = Path.Combine(
    Path.GetTempPath(),
    $"veil-legacy-profile-{Guid.NewGuid():N}");
var legacyProfileContainer = Path.Combine(legacyProfileRoot, "profile.veil");
var legacyWorkingProfile = Path.Combine(legacyProfileRoot, "working-profile");
try
{
    Directory.CreateDirectory(legacyProfileRoot);
    await using var legacyZip = new MemoryStream();
    using (var archive = new ZipArchive(legacyZip, ZipArchiveMode.Create, leaveOpen: true))
    {
        var entry = archive.CreateEntry("WebView2/legacy-cookie.txt");
        await using var writer = new StreamWriter(entry.Open(), Encoding.UTF8, leaveOpen: false);
        await writer.WriteAsync("0.3.0 profile data");
    }

    legacyZip.Position = 0;
    await using var v2Container = new MemoryStream();
    await ChunkedAesGcmFile.EncryptAsync(legacyZip, v2Container, key);
    var legacyBytes = v2Container.ToArray();
    legacyBytes[8] = 1;
    Array.Resize(ref legacyBytes, legacyBytes.Length - 16);
    await File.WriteAllBytesAsync(legacyProfileContainer, legacyBytes);

    await ProfileContainerService.RestoreAsync(
        legacyProfileContainer,
        legacyWorkingProfile,
        key);
    Require(
        await File.ReadAllTextAsync(Path.Combine(
            legacyWorkingProfile,
            "WebView2",
            "legacy-cookie.txt")) == "0.3.0 profile data",
        "A valid 0.3.0 profile container could not be migrated.");

    await ProfileContainerService.ProtectAsync(
        legacyWorkingProfile,
        legacyProfileContainer,
        key);
    var migratedHeader = await File.ReadAllBytesAsync(legacyProfileContainer);
    Require(migratedHeader[8] == 2, "Migrated profile container was not upgraded to v2.");
}
finally
{
    if (Directory.Exists(legacyProfileRoot))
    {
        Directory.Delete(legacyProfileRoot, recursive: true);
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

    var crashRecoveryMarker = Path.Combine(workingProfilePath, "newer-unsaved-data.txt");
    await File.WriteAllTextAsync(crashRecoveryMarker, "preserve me");
    await ProfileContainerService.RestoreAsync(
        profileContainerPath,
        workingProfilePath,
        key);
    Require(
        await File.ReadAllTextAsync(crashRecoveryMarker) == "preserve me",
        "A recoverable working profile was overwritten by an older container.");
}
finally
{
    if (Directory.Exists(profileTestRoot))
    {
        Directory.Delete(profileTestRoot, recursive: true);
    }
}

var maliciousProfileRoot = Path.Combine(
    Path.GetTempPath(),
    $"veil-malicious-profile-{Guid.NewGuid():N}");
var maliciousContainer = Path.Combine(maliciousProfileRoot, "profile.veil");
var maliciousWorking = Path.Combine(maliciousProfileRoot, "working-profile");
var escapedPath = Path.Combine(maliciousProfileRoot, "escape.txt");
try
{
    Directory.CreateDirectory(maliciousProfileRoot);
    await using var maliciousZip = new MemoryStream();
    using (var archive = new ZipArchive(maliciousZip, ZipArchiveMode.Create, leaveOpen: true))
    {
        var entry = archive.CreateEntry("../escape.txt");
        await using var writer = new StreamWriter(entry.Open(), Encoding.UTF8, leaveOpen: false);
        await writer.WriteAsync("must not escape");
    }
    maliciousZip.Position = 0;
    await using (var output = File.Create(maliciousContainer))
    {
        await ChunkedAesGcmFile.EncryptAsync(maliciousZip, output, profileKey);
    }

    await RequireThrowsAsync(
        () => ProfileContainerService.RestoreAsync(
            maliciousContainer,
            maliciousWorking,
            key),
        "A profile ZIP path traversal entry was accepted.",
        typeof(IOException));
    Require(!File.Exists(escapedPath), "Profile ZIP extraction escaped its destination.");
}
finally
{
    if (Directory.Exists(maliciousProfileRoot))
    {
        Directory.Delete(maliciousProfileRoot, recursive: true);
    }
}

CryptographicOperations.ZeroMemory(key);
CryptographicOperations.ZeroMemory(derived);
CryptographicOperations.ZeroMemory(expectedProfileData);
CryptographicOperations.ZeroMemory(stateKey);
CryptographicOperations.ZeroMemory(profileKey);
CryptographicOperations.ZeroMemory(wrapKey);
Console.WriteLine("All VeilBrowser core smoke tests passed.");
return;

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static async Task RequireThrowsAsync(
    Func<Task> action,
    string message,
    params Type[] expectedExceptionTypes)
{
    if (expectedExceptionTypes.Length == 0)
    {
        expectedExceptionTypes = [typeof(CryptographicException), typeof(EndOfStreamException)];
    }

    try
    {
        await action();
    }
    catch (Exception ex) when (expectedExceptionTypes.Any(type => type.IsInstanceOfType(ex)))
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static void RequireThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}
