namespace VeilBrowser.Security;

public sealed class SecurityMetadata
{
    public int Version { get; set; } = 1;
    public KeyProtectionMode Mode { get; set; }
    public bool StartupLock { get; set; } = true;
    public string SaltBase64 { get; set; } = string.Empty;
    public string VerifierBase64 { get; set; } = string.Empty;
    public string WrappedMasterKeyBase64 { get; set; } = string.Empty;
    public string DpapiProtectedKeyBase64 { get; set; } = string.Empty;
}
