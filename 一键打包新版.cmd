@echo off
setlocal
cd /d "%~dp0"
title VeilBrowser - Manual Signing Package

echo.
echo ============================================================
echo   VeilBrowser manual signing release packaging
echo ============================================================
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\manual-sign-package.ps1" -InstallCompiler
set "exitCode=%ERRORLEVEL%"

echo.
if not "%exitCode%"=="0" (
    echo Manual signing packaging stopped or failed. Exit code: %exitCode%
) else (
    echo Packaging completed successfully.
    echo Output: %~dp0artifacts\installer
)
echo.
pause
exit /b %exitCode%
