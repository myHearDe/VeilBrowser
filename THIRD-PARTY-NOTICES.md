# Third-Party Notices

VeilBrowser uses third-party components including:

- Microsoft.Web.WebView2 SDK and Microsoft Edge WebView2 Runtime.
- Chromium and bundled third-party libraries used by WebView2.
- Konscious.Security.Cryptography.Argon2.
- AdGuard Browser Extension 5.4.3.1, distributed as a separate unpacked
  Manifest V3 extension under the GNU General Public License v3.0.

Each component remains subject to its own license and notices. The WebView2
NuGet package includes `LICENSE.txt` and `NOTICE.txt`; release packaging must
retain the applicable Microsoft and Chromium notices.

The bundled AdGuard extension is located at `Extensions/AdGuard`. Its complete
GPLv3 license text and source reference are included as `LICENSE` and
`SOURCE.txt` in that directory. The exact corresponding upstream source archive
is included at
`ThirdPartySource/AdGuardBrowserExtension-source-v5.4.3.1.zip`. VeilBrowser's
MIT license does not replace or override the extension's GPLv3 terms.

References:

- https://www.nuget.org/packages/Microsoft.Web.WebView2
- https://developer.microsoft.com/microsoft-edge/webview2/
- https://github.com/kmaragon/Konscious.Security.Cryptography
- https://github.com/AdguardTeam/AdguardBrowserExtension/releases/tag/v5.4.3.1
