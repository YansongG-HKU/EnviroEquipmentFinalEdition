@echo off
chcp 65001 >nul
echo Adding 192.168.2.10/24 to Ethernet interface index 17...
netsh interface ipv4 add address name=17 address=192.168.2.10 mask=255.255.255.0
echo.
echo Current Ethernet IPv4 configuration:
netsh interface ipv4 show addresses name=17
echo.
pause
