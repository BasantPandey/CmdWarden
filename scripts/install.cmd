@echo off
setlocal
REM Double-click or: install.cmd
REM Optional args pass through to Install-CmdWarden.ps1 (e.g. -Version 0.1.0 -Force)

set "SCRIPT=%~dp0Install-CmdWarden.ps1"
if not exist "%SCRIPT%" (
  echo Install-CmdWarden.ps1 not found next to install.cmd
  exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" %*
set "ERR=%ERRORLEVEL%"
if not "%ERR%"=="0" (
  echo.
  echo Install failed with exit code %ERR%.
  pause
  exit /b %ERR%
)
echo.
pause
exit /b 0
