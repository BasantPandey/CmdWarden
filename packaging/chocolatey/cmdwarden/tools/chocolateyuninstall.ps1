$ErrorActionPreference = 'Continue'

Write-Host "Uninstalling CmdWarden global dotnet tool..."
foreach ($n in @('cw', 'cmdwarden', 'CmdWarden.Agent', 'CmdWarden.ApprovalGate', 'CmdWarden.SecretsManager')) {
  Get-Process -Name $n -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

# Remove Start Menu shortcut before cw.exe goes away (ticket #97).
$cw = Join-Path $env:USERPROFILE '.dotnet\tools\cw.exe'
if (Test-Path $cw) {
  cmd /c "`"$cw`" shortcut remove >nul 2>&1" | Out-Null
}
$vaultLnk = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\CmdWarden Vault.lnk'
Remove-Item -Force $vaultLnk -ErrorAction SilentlyContinue

cmd /c "dotnet tool uninstall -g CmdWarden >nul 2>&1" | Out-Null

$store = Join-Path $env:USERPROFILE '.dotnet\tools\.store\cmdwarden'
if (Test-Path $store) {
  Remove-Item -Recurse -Force $store -ErrorAction SilentlyContinue
}

$toolsPath = Join-Path $env:USERPROFILE '.dotnet\tools'
Remove-Item -Force (Join-Path $toolsPath 'cw.exe') -ErrorAction SilentlyContinue
Remove-Item -Force (Join-Path $toolsPath 'cmdwarden.exe') -ErrorAction SilentlyContinue

Write-Host "CmdWarden tool removed. Optional: delete %LOCALAPPDATA%\CmdWarden for policy/pins/audit data."
