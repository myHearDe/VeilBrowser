namespace VeilBrowser.Core.Models;

public sealed class SecurityPreferences
{
    public bool StartupLock { get; set; } = true;
    public int AutoLockMinutes { get; set; } = 10;
    public bool ClearCacheOnExit { get; set; }
    public bool BlockThirdPartyCookies { get; set; } = true;
    public bool TrackingProtectionEnabled { get; set; } = true;
    public bool WebRtcLeakProtection { get; set; } = true;
    public string HomePage { get; set; } = "https://www.bing.com";
    public Dictionary<LockArea, bool> AreaLocks { get; set; } = StandardLocks();

    public bool IsLocked(LockArea area) =>
        AreaLocks.TryGetValue(area, out var locked) && locked;

    public static Dictionary<LockArea, bool> StandardLocks() => new()
    {
        [LockArea.Browser] = true,
        [LockArea.History] = true,
        [LockArea.Downloads] = false,
        [LockArea.Bookmarks] = false,
        [LockArea.Passwords] = true,
        [LockArea.CookiesAndSiteData] = true,
        [LockArea.Sessions] = true,
        [LockArea.Autofill] = true,
        [LockArea.Settings] = true
    };
}
