[CmdletBinding()]
param(
    [switch]$InstallCompiler
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot "src\VeilBrowser\VeilBrowser.csproj"
$publishScript = Join-Path $PSScriptRoot "publish.ps1"
$buildInstallerScript = Join-Path $PSScriptRoot "build-installer.ps1"
$artifactRoot = Join-Path $projectRoot "artifacts"
$portableRoot = Join-Path $artifactRoot "VeilBrowser-win-x64"
$portableZip = Join-Path $artifactRoot "VeilBrowser-win-x64.zip"
$installerRoot = Join-Path $artifactRoot "installer"

function Get-ProjectVersion {
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

function Wait-ForManualContinue {
    param(
        [Parameter(Mandatory)]
        [string]$Prompt
    )

    while ($true) {
        Write-Host ""
        $answer = Read-Host "$Prompt`n输入 CONTINUE 或“继续”后按 Enter；输入 CANCEL 或“退出”终止"
        if ($answer -match '^(?i:CONTINUE|YES|Y)$' -or $answer -eq "继续") {
            return $true
        }
        if ($answer -match '^(?i:CANCEL|QUIT|EXIT|N|NO)$' -or $answer -eq "退出") {
            return $false
        }
        Write-Host "未识别指令，仍在等待签名完成。" -ForegroundColor Yellow
    }
}

function Open-ExplorerSelection {
    param([Parameter(Mandatory)][string]$Path)

    try {
        Start-Process -FilePath "explorer.exe" -ArgumentList "/select,`"$Path`""
    }
    catch {
        Write-Warning "无法自动打开资源管理器：$($_.Exception.Message)"
    }
}

function Get-SignTool {
    $command = Get-Command "signtool.exe" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $roots = @(
        (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"),
        (Join-Path $env:ProgramFiles "Windows Kits\10\bin")
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

    return $roots |
        ForEach-Object {
            Get-ChildItem -LiteralPath $_ -Recurse -Filter "signtool.exe" -File -ErrorAction SilentlyContinue
        } |
        Where-Object { $_.DirectoryName -match '\\x64$' } |
        Sort-Object FullName -Descending |
        Select-Object -ExpandProperty FullName -First 1
}

function Assert-ValidSignature {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [string]$SignTool
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "待验证文件不存在：$Path"
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw @"
签名验证失败：$Path
状态：$($signature.Status)
信息：$($signature.StatusMessage)
请重新签名并确保使用受信任的代码签名证书。
"@
    }
    if (-not $signature.SignerCertificate) {
        throw "文件没有可识别的签名证书：$Path"
    }
    if (-not $signature.TimeStamperCertificate) {
        throw "文件缺少受信任时间戳：$Path"
    }

    if ($SignTool) {
        & $SignTool verify /pa /v $Path | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "SignTool verification failed for '$Path' (exit code $LASTEXITCODE)."
        }
    }

    Write-Host "签名有效：$Path" -ForegroundColor Green
    Write-Host "签名者：$($signature.SignerCertificate.Subject)"
    Write-Host "时间戳：$($signature.TimeStamperCertificate.Subject)"
    return $signature
}

function Write-HashFile {
    param([Parameter(Mandatory)][string]$Path)

    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    "$hash  $(Split-Path -Leaf $Path)" |
        Set-Content -LiteralPath "$Path.sha256.txt" -Encoding ascii
    return $hash
}

Set-Location $projectRoot
$version = Get-ProjectVersion
$signTool = Get-SignTool

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " VeilBrowser $version 人工签名发布流程" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Warning "接下来会重新发布程序，现有发布目录中的旧签名会被覆盖。"

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $publishScript
if ($LASTEXITCODE -ne 0) {
    throw "Portable publish failed with exit code $LASTEXITCODE."
}

$applicationTargets = @(
    (Join-Path $portableRoot "VeilBrowser.exe"),
    (Join-Path $portableRoot "VeilBrowser.dll"),
    (Join-Path $portableRoot "VeilBrowser.Core.dll")
)

Write-Host ""
Write-Host "步骤 1/2：请使用你的签名工具签名以下三个文件，并添加 RFC 3161 时间戳：" -ForegroundColor Yellow
$applicationTargets | ForEach-Object { Write-Host "  $_" }
Open-ExplorerSelection -Path $applicationTargets[0]
$applicationSignatures = $null
$applicationThumbprints = $null
while (-not $applicationSignatures) {
    if (-not (Wait-ForManualContinue -Prompt "完成三个程序文件的签名后继续")) {
        throw "用户取消了人工签名打包。"
    }

    try {
        $verifiedSignatures = @(
            foreach ($target in $applicationTargets) {
                Assert-ValidSignature -Path $target -SignTool $signTool
            }
        )
        $verifiedThumbprints = @(
            $verifiedSignatures |
                ForEach-Object { $_.SignerCertificate.Thumbprint } |
                Sort-Object -Unique
        )
        if ($verifiedThumbprints.Count -ne 1) {
            throw "三个程序文件不是由同一张代码签名证书签名。"
        }

        $applicationSignatures = $verifiedSignatures
        $applicationThumbprints = $verifiedThumbprints
    }
    catch {
        Write-Host ""
        Write-Host "签名尚未通过验证，请修正后再次输入 CONTINUE。" -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "正在用已签名程序重建免安装 ZIP..." -ForegroundColor Cyan
Remove-Item -LiteralPath $portableZip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $portableRoot "*") -DestinationPath $portableZip -CompressionLevel Optimal
$portableZipHash = Write-HashFile -Path $portableZip

$installerArguments = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", $buildInstallerScript,
    "-SkipPublish",
    "-DisableSigning"
)
if ($InstallCompiler) {
    $installerArguments += "-InstallCompiler"
}

& powershell.exe @installerArguments
if ($LASTEXITCODE -ne 0) {
    throw "Installer compilation failed with exit code $LASTEXITCODE."
}

$installerPath = Join-Path $installerRoot "VeilBrowser-Setup-$version-x64.exe"
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "安装包没有生成：$installerPath"
}

Write-Host ""
Write-Host "步骤 2/2：请使用同一张证书签名最终安装包，并添加 RFC 3161 时间戳：" -ForegroundColor Yellow
Write-Host "  $installerPath"
Open-ExplorerSelection -Path $installerPath
$installerSignature = $null
while (-not $installerSignature) {
    if (-not (Wait-ForManualContinue -Prompt "完成安装包签名后继续")) {
        throw "用户取消了人工签名打包。"
    }

    try {
        $verifiedInstallerSignature = Assert-ValidSignature -Path $installerPath -SignTool $signTool
        if ($verifiedInstallerSignature.SignerCertificate.Thumbprint -ne $applicationThumbprints[0]) {
            throw "安装包与程序文件使用的不是同一张代码签名证书。"
        }
        $installerSignature = $verifiedInstallerSignature
    }
    catch {
        Write-Host ""
        Write-Host "安装包签名尚未通过验证，请修正后再次输入 CONTINUE。" -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Red
    }
}

$installerHash = Write-HashFile -Path $installerPath
$reportPath = Join-Path $installerRoot "VeilBrowser-$version-signature-report.txt"
$reportLines = @(
    "VeilBrowser $version signature report",
    "Generated: $([DateTime]::Now.ToString('yyyy-MM-dd HH:mm:ss zzz'))",
    "Signer: $($installerSignature.SignerCertificate.Subject)",
    "Signer thumbprint: $($installerSignature.SignerCertificate.Thumbprint)",
    "Timestamp authority: $($installerSignature.TimeStamperCertificate.Subject)",
    "",
    "Portable ZIP SHA256: $portableZipHash",
    "Portable ZIP: $portableZip",
    "Installer SHA256: $installerHash",
    "Installer: $installerPath"
)
$reportLines | Set-Content -LiteralPath $reportPath -Encoding UTF8

Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host " 人工签名打包完成，全部签名和时间戳验证通过" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
Write-Host "安装包：$installerPath"
Write-Host "安装包 SHA256：$installerHash"
Write-Host "免安装 ZIP：$portableZip"
Write-Host "免安装 ZIP SHA256：$portableZipHash"
Write-Host "签名报告：$reportPath"
