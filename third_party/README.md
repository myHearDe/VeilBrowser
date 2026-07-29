# Third-party build inputs

Large upstream AdGuard release artifacts are intentionally not stored in this
Git repository.

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\setup-adguard.ps1
```

The script downloads the pinned AdGuard Browser Extension `5.4.3.1` MV3 build
and its corresponding source archive from the official upstream GitHub
release, verifies both SHA-256 hashes, and prepares:

- `third_party\AdGuardBrowserExtension`
- `third_party\AdGuardBrowserExtension-source-v5.4.3.1.zip`

The normal build and publish scripts call this setup script automatically.
