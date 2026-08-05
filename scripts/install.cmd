@echo off
REM Root CLI installer for Windows CMD / PowerShell / VS Code terminal.
REM Usage:
REM   curl -fsSL https://raw.githubusercontent.com/botdev2/root-cli/main/scripts/install.cmd -o %TEMP%\root-install.cmd && %TEMP%\root-install.cmd
REM Or double-click / run this file after download.

setlocal
powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-RestMethod -Uri 'https://raw.githubusercontent.com/botdev2/root-cli/main/scripts/install.ps1' | Invoke-Expression"
if errorlevel 1 (
  echo.
  echo Install failed.
  exit /b 1
)
echo.
echo Done. Open a NEW terminal, then run:  rootcli here
exit /b 0
