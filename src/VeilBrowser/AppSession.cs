using System.Security.Cryptography;
using VeilBrowser.Core.Models;
using VeilBrowser.Core.Security;
using VeilBrowser.Infrastructure;
using VeilBrowser.Security;

namespace VeilBrowser;

public sealed class AppSession : IDisposable
{
    private readonly byte[] _masterKey;
    private readonly EncryptedJsonStore<BrowserState> _stateStore;
    private bool _disposed;

    public AppSession(
        AppPaths paths,
        SecurityBootstrapService security,
        byte[] masterKey,
        BrowserState state,
        bool hasMasterPassword)
    {
        Paths = paths;
        Security = security;
        _masterKey = masterKey;
        State = state;
        HasMasterPassword = hasMasterPassword;
        _stateStore = new EncryptedJsonStore<BrowserState>(paths.EncryptedState);
    }

    public AppPaths Paths { get; }
    public SecurityBootstrapService Security { get; }
    public BrowserState State { get; }
    public bool HasMasterPassword { get; private set; }
    public ReadOnlyMemory<byte> MasterKey => _masterKey;

    public Task SaveAsync(CancellationToken cancellationToken = default) =>
        _stateStore.SaveAsync(State, _masterKey, cancellationToken);

    public Task<bool> VerifyPasswordAsync(
        string password,
        CancellationToken cancellationToken = default) =>
        Security.VerifyPasswordAsync(password, cancellationToken);

    public void MarkMasterPasswordConfigured() => HasMasterPassword = true;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_masterKey);
        _disposed = true;
    }
}
