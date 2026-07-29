param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"

if (Get-Process -Name "VeilBrowser" -ErrorAction SilentlyContinue) {
    throw "隐栈浏览器仍在运行。请先正常关闭浏览器，等待加密完成后再清理。"
}

$dataRoot = Join-Path ([Environment]::GetFolderPath(
    [Environment+SpecialFolder]::LocalApplicationData)) "VeilBrowser"
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "VeilBrowser"

if (-not $Force) {
    Write-Host "即将永久删除隐栈浏览器的本地数据：" -ForegroundColor Yellow
    Write-Host "  $dataRoot"
    Write-Host "  $temporaryRoot"
    Write-Host ""
    Write-Host "包括历史、书签、密码库、Cookie、缓存、加密容器和安全设置。"
    Write-Host "普通“下载”文件夹中的文件不会被删除。"
    $answer = Read-Host "输入 DELETE 确认"
    if ($answer -cne "DELETE") {
        Write-Host "已取消，未删除任何数据。"
        exit 0
    }
}

foreach ($target in @($dataRoot, $temporaryRoot)) {
    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Recurse -Force
        Write-Host "已删除：$target" -ForegroundColor Green
    }
}

Write-Host "本地浏览器数据清理完成。" -ForegroundColor Green
