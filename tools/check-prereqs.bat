@echo off
chcp 65001 >nul
cd /d "%~dp0.."
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\tools\check-prereqs.ps1"
echo.
pause
