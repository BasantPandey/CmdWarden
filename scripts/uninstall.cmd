@echo off
setlocal
REM Double-click or: uninstall.cmd
REM Optional args pass through to Uninstall-CmdWarden.ps1 (e.g. -KeepData -RemoveSecrets)

set "SCRIPT=%~dp0Uninstall-CmdWarden.ps1"
if not exist "%SCRIPT%" (
  echo Uninstall-CmdWarden.ps1 not found next to uninstall.cmd
  exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" %*
exit /b %ERRORLEVEL%
