@echo off
rem Double-click launcher for install-server.ps1 (a .ps1 file does not run on double-click).
rem Keep it in the same folder as install-server.ps1 and ClubControl.Server.exe.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-server.ps1" %*
pause
