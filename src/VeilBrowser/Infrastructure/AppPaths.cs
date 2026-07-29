using System.IO;

namespace VeilBrowser.Infrastructure;

public sealed class AppPaths
{
    public AppPaths()
    {
        DataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VeilBrowser");
        WorkingProfile = Path.Combine(Path.GetTempPath(), "VeilBrowser", "working-profile");
        EncryptedProfile = Path.Combine(DataRoot, "profile.veil");
        EncryptedState = Path.Combine(DataRoot, "browser-state.veil");
        SecurityMetadata = Path.Combine(DataRoot, "security.json");
        ForceLockMarker = Path.Combine(DataRoot, "force-lock");
        AdGuardExtension = Path.Combine(
            AppContext.BaseDirectory,
            "Extensions",
            "AdGuard");
        AdGuardInstallMarker = Path.Combine(
            WorkingProfile,
            "adguard-extension.json");
        Directory.CreateDirectory(DataRoot);
    }

    public string DataRoot { get; }
    public string WorkingProfile { get; }
    public string EncryptedProfile { get; }
    public string EncryptedState { get; }
    public string SecurityMetadata { get; }
    public string ForceLockMarker { get; }
    public string AdGuardExtension { get; }
    public string AdGuardInstallMarker { get; }
}
