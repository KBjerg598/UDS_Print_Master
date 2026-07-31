@echo off
setlocal

powershell.exe -NoProfile -ExecutionPolicy Bypass ^
  -File "%~dp0Build-Package.ps1" %*

exit /b %errorlevel%
