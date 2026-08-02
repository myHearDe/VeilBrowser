using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace VeilBrowser.Infrastructure;

public sealed class AppPaths
{
    public AppPaths()
    {
        DataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VeilBrowser");
        // WebView2 keeps live cookies/cache in this directory while the browser
        // is unlocked. Keep it under the private application root instead of
        // the shared TEMP directory where another local process could inspect it.
        WorkingProfile = Path.Combine(DataRoot, "working-profile");
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
        RestrictDirectory(DataRoot);
    }

    public string DataRoot { get; }
    public string WorkingProfile { get; }
    public string EncryptedProfile { get; }
    public string EncryptedState { get; }
    public string SecurityMetadata { get; }
    public string ForceLockMarker { get; }
    public string AdGuardExtension { get; }
    public string AdGuardInstallMarker { get; }

    private static void RestrictDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current Windows identity is unavailable.");
        var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.FullControl,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));

        new DirectoryInfo(path).SetAccessControl(security);
    }
}
