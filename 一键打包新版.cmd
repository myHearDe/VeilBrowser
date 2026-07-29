@echo off
setlocal
cd /d "%~dp0"
title VeilBrowser - Build Installer

echo.
echo ============================================================
echo   VeilBrowser one-click release packaging
echo ============================================================
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build-installer.ps1" -InstallCompiler
set "exitCode=%ERRORLEVEL%"

echo.
if not "%exitCode%"=="0" (
    echo Packaging failed. Exit code: %exitCode%
) else (
    echo Packaging completed successfully.
    echo Output: %~dp0artifacts\installer
)
echo.
pause
exit /b %exitCode%
