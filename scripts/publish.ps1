$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $projectRoot "artifacts"
$publishRoot = Join-Path $artifactRoot "VeilBrowser-win-x64"
$zipPath = Join-Path $artifactRoot "VeilBrowser-win-x64.zip"
Set-Location $projectRoot

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "setup-adguard.ps1")
if ($LASTEXITCODE -ne 0) {
    throw "AdGuard setup failed with exit code $LASTEXITCODE."
}

if (Test-Path $publishRoot) {
    Remove-Item $publishRoot -Recurse -Force
}
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

dotnet restore .\VeilBrowser.slnx
if ($LASTEXITCODE -ne 0) {
    throw "Solution restore failed with exit code $LASTEXITCODE."
}

dotnet restore .\src\VeilBrowser\VeilBrowser.csproj -r win-x64
if ($LASTEXITCODE -ne 0) {
    throw "Windows x64 runtime restore failed with exit code $LASTEXITCODE."
}

dotnet run --project .\tests\VeilBrowser.Core.SmokeTests\VeilBrowser.Core.SmokeTests.csproj -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Core smoke tests failed with exit code $LASTEXITCODE."
}

dotnet publish .\src\VeilBrowser\VeilBrowser.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    --no-restore `
    -p:PublishSingleFile=false `
    -p:RequireAdGuardSourceArchive=true `
    -p:BaseOutputPath="$artifactRoot\publish-build\" `
    -o $publishRoot
if ($LASTEXITCODE -ne 0) {
    throw "Portable publish failed with exit code $LASTEXITCODE."
}

Copy-Item .\README.md $publishRoot
Copy-Item .\LICENSE $publishRoot
Copy-Item .\LICENSE-SCOPE.md $publishRoot
Copy-Item .\THIRD-PARTY-NOTICES.md $publishRoot
Copy-Item .\scripts\clean-local-data.ps1 (Join-Path $publishRoot "Clean-Local-Data.ps1")
Compress-Archive -Path (Join-Path $publishRoot "*") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host ""
Write-Host "Portable build: $publishRoot" -ForegroundColor Green
Write-Host "ZIP package:    $zipPath" -ForegroundColor Green
