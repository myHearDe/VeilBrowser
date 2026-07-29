[CmdletBinding()]
param(
    [switch]$SkipPublish,
    [switch]$InstallCompiler
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot "src\VeilBrowser\VeilBrowser.csproj"
$installerScript = Join-Path $projectRoot "installer\VeilBrowser.iss"
$publishScript = Join-Path $PSScriptRoot "publish.ps1"
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
if (-not (Test-Path -LiteralPath $portableExe)) {
    throw "Portable build not found: $portableExe. Run without -SkipPublish first."
}

$version = Get-ProjectVersion
$versionInfo = Get-VersionInfoVersion $version

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

$hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
$hashFile = "$installerPath.sha256.txt"
"$hash  $(Split-Path -Leaf $installerPath)" | Set-Content -LiteralPath $hashFile -Encoding ascii

Write-Host ""
Write-Host "Installer: $installerPath" -ForegroundColor Green
Write-Host "SHA256:   $hash" -ForegroundColor Green
Write-Host "Hash file: $hashFile" -ForegroundColor Green
