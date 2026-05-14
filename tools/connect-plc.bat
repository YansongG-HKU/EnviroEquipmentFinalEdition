@echo off
chcp 65001 >nul
cd /d "%~dp0.."
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\tools\connect-plc.ps1" -Ip 192.168.2.180
echo.
pause
