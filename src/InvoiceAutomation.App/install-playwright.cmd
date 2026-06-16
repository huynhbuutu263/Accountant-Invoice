@echo off
REM Installs Playwright Chromium without changing system PowerShell execution policy.
setlocal
cd /d "%~dp0"

if not exist "playwright.ps1" (
    echo ERROR: playwright.ps1 not found in:
    echo   %~dp0
    echo Build or publish InvoiceAutomation.App first, then run this script from the output folder.
    exit /b 1
)

echo Installing Playwright browsers...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0playwright.ps1" install
if errorlevel 1 (
    echo Install failed.
    exit /b 1
)

echo Done. You can run InvoiceAutomation.App.exe now.
endlocal
