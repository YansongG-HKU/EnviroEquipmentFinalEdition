@echo off
chcp 65001 >nul
cd /d "%~dp0.."
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\tools\run-s7-demo.ps1" --adapter snap7 --read-once
echo.
pause
