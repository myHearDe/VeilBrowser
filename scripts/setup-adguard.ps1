[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$SkipSourceArchive
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$thirdPartyRoot = Join-Path $projectRoot "third_party"
$extensionRoot = Join-Path $thirdPartyRoot "AdGuardBrowserExtension"
$manifestPath = Join-Path $extensionRoot "manifest.json"
$sourceArchive = Join-Path $thirdPartyRoot "AdGuardBrowserExtension-source-v5.4.3.1.zip"

$version = "5.4.3.1"
$extensionUrl = "https://github.com/AdguardTeam/AdguardBrowserExtension/releases/download/v$version/chrome-mv3.zip"
$extensionSha256 = "C91CBB56BBAACC96CB7B9554D9728158CE1791E02895EA5CA5D909CD4764C2F1"
$sourceUrl = "https://github.com/AdguardTeam/AdguardBrowserExtension/archive/refs/tags/v$version.zip"
$sourceSha256 = "17B92B201B69F1F8B6304C8E46DB51DA11F1EAB6093260AF082D4B4F5C410F0E"
$licenseUrl = "https://raw.githubusercontent.com/AdguardTeam/AdguardBrowserExtension/v$version/LICENSE"
$licenseSha256 = "3972DC9744F6499F0F9B2DBF76696F2AE7AD8AF9B23DDE66D6AF86C9DFB36986"

function Assert-FileHash {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [string]$Expected
    )

    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if ($actual -ne $Expected) {
        throw "SHA256 mismatch for '$Path'. Expected $Expected, got $actual."
    }
}

function Download-VerifiedFile {
    param(
        [Parameter(Mandatory)]
        [string]$Uri,
        [Parameter(Mandatory)]
        [string]$Destination,
        [Parameter(Mandatory)]
        [string]$Sha256
    )

    $temporary = "$Destination.download"
    Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
    Write-Host "Downloading $Uri" -ForegroundColor Cyan
    Invoke-WebRequest -Uri $Uri -UseBasicParsing -OutFile $temporary
    Assert-FileHash -Path $temporary -Expected $Sha256
    Move-Item -LiteralPath $temporary -Destination $Destination -Force
}

New-Item -ItemType Directory -Path $thirdPartyRoot -Force | Out-Null

$extensionReady = Test-Path -LiteralPath $manifestPath
if ($extensionReady -and -not $Force) {
    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 |
            ConvertFrom-Json
        $extensionReady = $manifest.version -eq $version
    }
    catch {
        $extensionReady = $false
    }
}

if (-not $extensionReady -or $Force) {
    $extensionArchive = Join-Path $thirdPartyRoot "AdGuardBrowserExtension-$version.zip"
    Download-VerifiedFile -Uri $extensionUrl -Destination $extensionArchive -Sha256 $extensionSha256

    $extractRoot = Join-Path $thirdPartyRoot ".adguard-extract-$version"
    Remove-Item -LiteralPath $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
    Expand-Archive -LiteralPath $extensionArchive -DestinationPath $extractRoot -Force

    Remove-Item -LiteralPath $extensionRoot -Recurse -Force -ErrorAction SilentlyContinue
    Move-Item -LiteralPath $extractRoot -Destination $extensionRoot
    Remove-Item -LiteralPath $extensionArchive -Force
}

$sourceReady = $false
if (-not $SkipSourceArchive) {
    $sourceReady = Test-Path -LiteralPath $sourceArchive
    if ($sourceReady -and -not $Force) {
        try {
            Assert-FileHash -Path $sourceArchive -Expected $sourceSha256
        }
        catch {
            $sourceReady = $false
        }
    }

    if (-not $sourceReady -or $Force) {
        Download-VerifiedFile -Uri $sourceUrl -Destination $sourceArchive -Sha256 $sourceSha256
    }
}

# The release ZIP does not carry the repository license file. Full packaging
# extracts it from the matching source archive; normal builds download only the
# small, pinned license so CI does not need the ~100 MB source archive.
$licensePath = Join-Path $extensionRoot "LICENSE"
if ($SkipSourceArchive) {
    if ($Force -or -not (Test-Path -LiteralPath $licensePath)) {
        Download-VerifiedFile -Uri $licenseUrl -Destination $licensePath -Sha256 $licenseSha256
    }
    Assert-FileHash -Path $licensePath -Expected $licenseSha256
}
else {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $sourceZip = [System.IO.Compression.ZipFile]::OpenRead($sourceArchive)
    try {
        $licenseEntry = $sourceZip.Entries |
            Where-Object { $_.FullName -match '/LICENSE$' } |
            Select-Object -First 1
        if (-not $licenseEntry) {
            throw "The pinned source archive does not contain LICENSE."
        }
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile(
            $licenseEntry,
            $licensePath,
            $true
        )
    }
    finally {
        $sourceZip.Dispose()
    }
    Assert-FileHash -Path $licensePath -Expected $licenseSha256
}

$sourceText = @"
AdGuard Browser Extension $version
Source: https://github.com/AdguardTeam/AdguardBrowserExtension/releases/tag/v$version
Bundled artifact: chrome-mv3.zip
Artifact SHA256: $extensionSha256
License: GNU General Public License v3.0 (see LICENSE)
The extension is distributed as a separate unpacked browser extension and remains governed by its own license.
Corresponding source archive: ../AdGuardBrowserExtension-source-v$version.zip
Source archive SHA256: $sourceSha256
"@
$sourceText | Set-Content -LiteralPath (Join-Path $extensionRoot "SOURCE.txt") -Encoding UTF8

if (-not $SkipSourceArchive) {
    Assert-FileHash -Path $sourceArchive -Expected $sourceSha256
}
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "AdGuard setup did not produce $manifestPath."
}

Write-Host "AdGuard Browser Extension $version is ready." -ForegroundColor Green
