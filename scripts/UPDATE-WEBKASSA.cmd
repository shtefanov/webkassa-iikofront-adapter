@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-webkassa-update.ps1" -Channel beta -WaitForKey
exit /b %errorlevel%
