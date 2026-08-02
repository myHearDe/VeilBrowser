# VeilBrowser AdGuard integration bridge

This directory contains the VeilBrowser-specific integration source layered on
top of AdGuard Browser Extension 5.4.3.1.

- `pages/veil-control.*` replaces direct navigation to AdGuard's toolbar popup.
  A toolbar popup assumes a browser-owned active-tab context, which a standalone
  WebView2 controller does not provide.
- `patches/assistant-explicit-tab.patch` lets the integration request AdGuard's
  element picker for an explicit WebView2 tab id.

The corresponding unmodified upstream source archive is downloaded with a
pinned SHA-256 by `scripts/setup-adguard.ps1` and is included in release source
artifacts as `ThirdPartySource/AdGuardBrowserExtension-source-v5.4.3.1.zip`.
That archive plus the bridge files in this directory are the corresponding
source for the bundled modified extension without duplicating a 100 MB upstream
archive in Git history.

`scripts/setup-adguard.ps1` copies these pages and applies the compiled
background integration after every verified upstream download, so a clean
clone produces the same bundled extension instead of silently losing the fix.

The bridge is distributed under GNU GPLv3 together with the modified AdGuard
extension. See `LICENSE` in this directory.
