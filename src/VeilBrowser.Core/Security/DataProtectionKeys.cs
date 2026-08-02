using System.Security.Cryptography;
using System.Text;

namespace VeilBrowser.Core.Security;

public static class DataProtectionKeys
{
    public const string BrowserStateContext = "VeilBrowser/browser-state/v2";
    public const string ProfileContainerContext = "VeilBrowser/profile-container/v2";
    public const string MasterKeyWrapContext = "VeilBrowser/master-key-wrap/v2";

    private static readonly byte[] ExtractSalt =
        SHA256.HashData("VeilBrowser key separation salt v1"u8);

    public static byte[] Derive(ReadOnlySpan<byte> masterKey, string context)
    {
        if (masterKey.Length < 16)
        {
            throw new ArgumentException("Master key must contain at least 16 bytes.", nameof(masterKey));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(context);

        var inputKey = masterKey.ToArray();
        var contextBytes = Encoding.UTF8.GetBytes(context);
        var expandInput = new byte[contextBytes.Length + 1];
        contextBytes.CopyTo(expandInput, 0);
        expandInput[^1] = 1;
        byte[]? pseudoRandomKey = null;
        try
        {
            pseudoRandomKey = HMACSHA256.HashData(ExtractSalt, inputKey);
            return HMACSHA256.HashData(pseudoRandomKey, expandInput);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(inputKey);
            CryptographicOperations.ZeroMemory(contextBytes);
            CryptographicOperations.ZeroMemory(expandInput);
            if (pseudoRandomKey is not null)
            {
                CryptographicOperations.ZeroMemory(pseudoRandomKey);
            }
        }
    }
}
