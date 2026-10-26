@echo off
rem Double-click launcher for install-client.ps1 (a .ps1 file does not run on double-click).
rem Keep it in the same folder as install-client.ps1 and ClubControl.Client.exe.
rem
rem Optional: pass the auto-logon account from a command prompt, e.g.
rem   install-client.bat -UserId ClubPC1
rem Without it, the task is created for the account you are logged in as right now.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-client.ps1" %*
pause
