$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location $projectRoot

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "setup-adguard.ps1") -SkipSourceArchive
if ($LASTEXITCODE -ne 0) {
    throw "AdGuard setup failed with exit code $LASTEXITCODE."
}

dotnet --info
if ($LASTEXITCODE -ne 0) {
    throw "Unable to query the .NET SDK. Exit code: $LASTEXITCODE."
}

dotnet restore .\VeilBrowser.slnx
if ($LASTEXITCODE -ne 0) {
    throw "Solution restore failed with exit code $LASTEXITCODE."
}

dotnet build .\VeilBrowser.slnx -c Release --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "Release build failed with exit code $LASTEXITCODE."
}

dotnet run --project .\tests\VeilBrowser.Core.SmokeTests\VeilBrowser.Core.SmokeTests.csproj -c Release --no-build
if ($LASTEXITCODE -ne 0) {
    throw "Core smoke tests failed with exit code $LASTEXITCODE."
}

dotnet run --project .\tests\VeilBrowser.Ui.SmokeTests\VeilBrowser.Ui.SmokeTests.csproj -c Release --no-build
if ($LASTEXITCODE -ne 0) {
    throw "UI theme smoke tests failed with exit code $LASTEXITCODE."
}

Write-Host ""
Write-Host "Build and all smoke tests completed successfully." -ForegroundColor Green
