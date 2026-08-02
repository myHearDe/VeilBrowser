[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]]$Path,

    [string]$PfxPath,
    [string]$CertificateThumbprint,

    [ValidateSet("CurrentUser", "LocalMachine")]
    [string]$CertificateStoreLocation = "CurrentUser",

    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [securestring]$PfxPassword
)

$ErrorActionPreference = "Stop"

function Find-SignTool {
    $command = Get-Command "signtool.exe" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $roots = @(
        (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"),
        (Join-Path $env:ProgramFiles "Windows Kits\10\bin")
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

    $candidates = foreach ($root in $roots) {
        Get-ChildItem -LiteralPath $root -Recurse -Filter "signtool.exe" -File -ErrorAction SilentlyContinue |
            Where-Object { $_.DirectoryName -match '\\(x64|x86)$' }
    }

    return $candidates |
        Sort-Object @{ Expression = {
            if ($_.DirectoryName -match '\\x64$') { 1 } else { 0 }
        }; Descending = $true }, VersionInfo -Descending |
        Select-Object -ExpandProperty FullName -First 1
}

function ConvertTo-PlainText {
    param([Parameter(Mandatory)][securestring]$SecureValue)

    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

$signTool = Find-SignTool
if (-not $signTool) {
    throw @"
signtool.exe was not found.
Install the Windows 10/11 SDK Signing Tools component from Visual Studio Installer,
then rerun the packaging command.
"@
}

if ($PfxPath -and $CertificateThumbprint) {
    throw "Choose either PfxPath or CertificateThumbprint, not both."
}
if (-not $PfxPath -and -not $CertificateThumbprint) {
    throw "No signing identity was configured. Set PfxPath or CertificateThumbprint."
}
if ([string]::IsNullOrWhiteSpace($TimestampUrl)) {
    throw "TimestampUrl cannot be empty for a release signature."
}

$resolvedTargets = foreach ($target in $Path) {
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
        throw "Signing target not found: $target"
    }
    (Resolve-Path -LiteralPath $target).Path
}

$certificateArgs = @()
$plainPassword = $null
if ($PfxPath) {
    if (-not (Test-Path -LiteralPath $PfxPath -PathType Leaf)) {
        throw "PFX file not found: $PfxPath"
    }

    if (-not $PfxPassword) {
        if ($env:VEIL_SIGN_PFX_PASSWORD) {
            $PfxPassword = ConvertTo-SecureString $env:VEIL_SIGN_PFX_PASSWORD -AsPlainText -Force
        }
        else {
            $PfxPassword = Read-Host "请输入代码签名 PFX 密码" -AsSecureString
        }
    }

    $plainPassword = ConvertTo-PlainText $PfxPassword
    $certificateArgs = @("/f", (Resolve-Path -LiteralPath $PfxPath).Path, "/p", $plainPassword)
}
else {
    $thumbprint = $CertificateThumbprint.Replace(" ", "").ToUpperInvariant()
    if ($thumbprint -notmatch '^[0-9A-F]{40}$') {
        throw "CertificateThumbprint must be a 40-character SHA-1 thumbprint."
    }

    $storePath = "Cert:\$CertificateStoreLocation\My\$thumbprint"
    $certificate = Get-Item -LiteralPath $storePath -ErrorAction SilentlyContinue
    if (-not $certificate) {
        throw "Code-signing certificate was not found: $storePath"
    }
    if (-not $certificate.HasPrivateKey) {
        throw "The selected certificate does not have an accessible private key."
    }
    if ($certificate.NotAfter -le (Get-Date)) {
        throw "The selected certificate has expired: $($certificate.NotAfter)"
    }

    $certificateArgs = @("/sha1", $thumbprint, "/s", "My")
    if ($CertificateStoreLocation -eq "LocalMachine") {
        $certificateArgs += "/sm"
    }
}

try {
    foreach ($target in $resolvedTargets) {
        Write-Host "Signing: $target" -ForegroundColor Cyan
        $arguments = @(
            "sign",
            "/v",
            "/fd", "SHA256",
            "/tr", $TimestampUrl,
            "/td", "SHA256"
        ) + $certificateArgs + @($target)

        & $signTool @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "SignTool failed to sign '$target' (exit code $LASTEXITCODE)."
        }

        & $signTool verify /pa /v $target
        if ($LASTEXITCODE -ne 0) {
            throw "SignTool verification failed for '$target' (exit code $LASTEXITCODE)."
        }

        $signature = Get-AuthenticodeSignature -LiteralPath $target
        if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
            throw "PowerShell Authenticode verification failed for '$target': $($signature.StatusMessage)"
        }
        if (-not $signature.TimeStamperCertificate) {
            throw "The signature on '$target' does not contain a trusted timestamp."
        }

        Write-Host "Verified signer: $($signature.SignerCertificate.Subject)" -ForegroundColor Green
        Write-Host "Timestamp issuer: $($signature.TimeStamperCertificate.Issuer)" -ForegroundColor Green
    }
}
finally {
    $plainPassword = $null
    $PfxPassword = $null
}
