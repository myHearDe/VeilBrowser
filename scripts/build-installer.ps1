[CmdletBinding()]
param(
    [switch]$SkipPublish,
    [switch]$InstallCompiler,
    [switch]$Sign,
    [switch]$DisableSigning,
    [string]$PfxPath,
    [string]$CertificateThumbprint,
    [ValidateSet("CurrentUser", "LocalMachine")]
    [string]$CertificateStoreLocation = "CurrentUser",
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot "src\VeilBrowser\VeilBrowser.csproj"
$installerScript = Join-Path $projectRoot "installer\VeilBrowser.iss"
$publishScript = Join-Path $PSScriptRoot "publish.ps1"
$signScript = Join-Path $PSScriptRoot "sign-files.ps1"
$signingConfigPath = Join-Path $projectRoot "signing.local.ps1"
$outputRoot = Join-Path $projectRoot "artifacts\installer"

function Find-InnoCompiler {
    $command = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

    return $candidates | Select-Object -First 1
}

function Get-ProjectVersion {
    # Windows PowerShell 5.1 may treat UTF-8 without BOM as the active ANSI
    # code page, which corrupts Chinese XML text before parsing.
    [xml]$projectXml = [System.IO.File]::ReadAllText(
        $projectFile,
        [System.Text.Encoding]::UTF8
    )
    $version = @(
        $projectXml.Project.PropertyGroup.Version |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    ) | Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "Unable to read <Version> from $projectFile."
    }

    return [string]$version
}

function Get-VersionInfoVersion([string]$Version) {
    $numeric = ($Version -split '[-+]')[0]
    $parts = @($numeric -split '\.')

    if ($parts.Count -gt 4 -or ($parts | Where-Object { $_ -notmatch '^\d+$' })) {
        throw "Project version '$Version' cannot be converted to a Windows four-part version."
    }

    while ($parts.Count -lt 4) {
        $parts += "0"
    }

    return ($parts -join '.')
}

if ($Sign -and $DisableSigning) {
    throw "Sign and DisableSigning cannot be used together."
}

if (-not $DisableSigning -and (Test-Path -LiteralPath $signingConfigPath)) {
    $signingConfig = & $signingConfigPath
    if ($signingConfig -isnot [hashtable]) {
        throw "signing.local.ps1 must return a hashtable. See signing.example.ps1."
    }

    if (-not $PSBoundParameters.ContainsKey("PfxPath")) {
        $PfxPath = [string]$signingConfig.PfxPath
    }
    if (-not $PSBoundParameters.ContainsKey("CertificateThumbprint")) {
        $CertificateThumbprint = [string]$signingConfig.CertificateThumbprint
    }
    if (-not $PSBoundParameters.ContainsKey("CertificateStoreLocation") -and
        $signingConfig.CertificateStoreLocation) {
        $CertificateStoreLocation = [string]$signingConfig.CertificateStoreLocation
    }
    if (-not $PSBoundParameters.ContainsKey("TimestampUrl") -and $signingConfig.TimestampUrl) {
        $TimestampUrl = [string]$signingConfig.TimestampUrl
    }

    $Sign = $true
}

if (-not $DisableSigning -and -not $PfxPath -and $env:VEIL_SIGN_PFX) {
    $PfxPath = $env:VEIL_SIGN_PFX
    $Sign = $true
}
if (-not $DisableSigning -and -not $CertificateThumbprint -and $env:VEIL_SIGN_THUMBPRINT) {
    $CertificateThumbprint = $env:VEIL_SIGN_THUMBPRINT
    $Sign = $true
}
if (-not $DisableSigning -and $env:VEIL_SIGN_TIMESTAMP_URL) {
    $TimestampUrl = $env:VEIL_SIGN_TIMESTAMP_URL
}
if (-not $DisableSigning -and ($PfxPath -or $CertificateThumbprint)) {
    $Sign = $true
}

$iscc = Find-InnoCompiler
if (-not $iscc -and $InstallCompiler) {
    Write-Host "Installing Inno Setup 6 with winget..." -ForegroundColor Cyan
    winget install --id JRSoftware.InnoSetup --exact --silent `
        --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) {
        throw "winget failed to install Inno Setup (exit code $LASTEXITCODE)."
    }
    $iscc = Find-InnoCompiler
}

if (-not $iscc) {
    throw @"
Inno Setup 6 was not found.
Install it with:
  winget install --id JRSoftware.InnoSetup --exact --silent --accept-package-agreements --accept-source-agreements
Or rerun this script with -InstallCompiler.
"@
}

if (-not $SkipPublish) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $publishScript
    if ($LASTEXITCODE -ne 0) {
        throw "Portable publish failed with exit code $LASTEXITCODE."
    }
}

$portableExe = Join-Path $projectRoot "artifacts\VeilBrowser-win-x64\VeilBrowser.exe"
$portableRoot = Split-Path -Parent $portableExe
$portableZip = Join-Path $projectRoot "artifacts\VeilBrowser-win-x64.zip"
if (-not (Test-Path -LiteralPath $portableExe)) {
    throw "Portable build not found: $portableExe. Run without -SkipPublish first."
}

$version = Get-ProjectVersion
$versionInfo = Get-VersionInfoVersion $version

if ($Sign) {
    $portableSignTargets = @(
        $portableExe,
        (Join-Path (Split-Path -Parent $portableExe) "VeilBrowser.dll"),
        (Join-Path (Split-Path -Parent $portableExe) "VeilBrowser.Core.dll")
    )

    $signParameters = @{
        Path = $portableSignTargets
        CertificateStoreLocation = $CertificateStoreLocation
        TimestampUrl = $TimestampUrl
    }
    if ($PfxPath) {
        $signParameters.PfxPath = $PfxPath
    }
    if ($CertificateThumbprint) {
        $signParameters.CertificateThumbprint = $CertificateThumbprint
    }

    Write-Host "Signing application files before installer compilation..." -ForegroundColor Cyan
    & $signScript @signParameters

    # publish.ps1 creates the portable ZIP before signing. Recreate it so the
    # downloadable ZIP contains the exact signed files used by the installer.
    Remove-Item -LiteralPath $portableZip -Force -ErrorAction SilentlyContinue
    Compress-Archive -Path (Join-Path $portableRoot "*") -DestinationPath $portableZip -CompressionLevel Optimal
    $portableZipHash = (Get-FileHash -LiteralPath $portableZip -Algorithm SHA256).Hash
    "$portableZipHash  $(Split-Path -Leaf $portableZip)" |
        Set-Content -LiteralPath "$portableZip.sha256.txt" -Encoding ascii
    Write-Host "Signed portable ZIP: $portableZip" -ForegroundColor Green
}
else {
    if ($DisableSigning) {
        Write-Host "Automatic signing is disabled for the manual-signing workflow." -ForegroundColor Yellow
    }
    else {
        Write-Warning "Signing is not configured. The application and installer will be unsigned."
    }
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

Write-Host "Compiling installer with $iscc" -ForegroundColor Cyan
& $iscc "/DMyAppVersion=$version" "/DMyAppVersionInfo=$versionInfo" $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compiler failed with exit code $LASTEXITCODE."
}

$installerPath = Join-Path $outputRoot "VeilBrowser-Setup-$version-x64.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Expected installer was not produced: $installerPath"
}

if ($Sign) {
    Write-Host "Signing the final installer..." -ForegroundColor Cyan
    $installerSignParameters = $signParameters.Clone()
    $installerSignParameters.Path = @($installerPath)
    & $signScript @installerSignParameters
}

$hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
$hashFile = "$installerPath.sha256.txt"
"$hash  $(Split-Path -Leaf $installerPath)" | Set-Content -LiteralPath $hashFile -Encoding ascii

Write-Host ""
Write-Host "Installer: $installerPath" -ForegroundColor Green
Write-Host "SHA256:   $hash" -ForegroundColor Green
Write-Host "Hash file: $hashFile" -ForegroundColor Green
