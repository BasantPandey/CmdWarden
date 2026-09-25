#Requires -Version 5.1
<#
.SYNOPSIS
  Remove CmdWarden from this Windows user account.

.DESCRIPTION
  Works when the cw command is broken or gone. Each step runs on its own.
  A failed step prints a warning and the next step continues.

  Steps:
    1. Unharden gh, git, and docker, so their logins go back to the stock stores.
    2. Stop the Session Agent, the Approval Gate, and CmdWarden Vault.
    3. Remove the Start Menu entry and the Desktop icon.
    4. Uninstall the dotnet tool and clear its tool store.
    5. Remove CmdWarden folders from PATH.
    6. Delete local data: policy, pins, shims, audit (skip with -KeepData).
    7. Delete saved secrets from Credential Manager (only with -RemoveSecrets or a yes).
    8. Remove the entry in Windows Settings > Apps.

  Windows Settings > Apps > CmdWarden > Uninstall runs this script.

.PARAMETER Quiet
  Ask nothing. Keep saved secrets unless -RemoveSecrets is set. Skip the admin PATH step.

.PARAMETER KeepData
  Keep %LOCALAPPDATA%\CmdWarden (policy, pins, audit). Use it before a reinstall.

.PARAMETER RemoveSecrets
  Also delete every CmdWarden entry in Windows Credential Manager.

.EXAMPLE
  .\scripts\Uninstall-CmdWarden.ps1

.EXAMPLE
  .\scripts\Uninstall-CmdWarden.ps1 -Quiet -RemoveSecrets
#>
[CmdletBinding()]
param(
    [switch] $Quiet,
    [switch] $KeepData,
    [switch] $RemoveSecrets
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Continue"

$ProductRoot = if ($env:CW_PRODUCT_ROOT) { $env:CW_PRODUCT_ROOT } else { Join-Path $env:LOCALAPPDATA "CmdWarden" }
$ToolsDir = Join-Path $env:USERPROFILE ".dotnet\tools"
$ToolStore = Join-Path $ToolsDir ".store\cmdwarden"
$UninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CmdWarden"
$script:Warnings = New-Object System.Collections.Generic.List[string]

function Write-Step([string] $Message) {
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Write-Ok([string] $Message) {
    Write-Host "    $Message" -ForegroundColor Green
}

function Write-Warn([string] $Message) {
    Write-Host "    $Message" -ForegroundColor Yellow
    $script:Warnings.Add($Message)
}

function Confirm-Step([string] $Question, [bool] $Default) {
    if ($Quiet) { return $Default }
    $hint = if ($Default) { "[Y/n]" } else { "[y/N]" }
    $answer = Read-Host "$Question $hint"
    if ([string]::IsNullOrWhiteSpace($answer)) { return $Default }
    return $answer.Trim().ToLowerInvariant().StartsWith("y")
}

# A cw that answers "cw version". Order: tool shim, tool store dll, portable copy.
function Find-Cw {
    $candidates = New-Object System.Collections.Generic.List[object]
    $shim = Join-Path $ToolsDir "cw.exe"
    if (Test-Path $shim) { $candidates.Add(@($shim)) }
    if (Test-Path $ToolStore) {
        Get-ChildItem $ToolStore -Recurse -Filter cw.dll -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            ForEach-Object { $candidates.Add(@("dotnet", $_.FullName)) }
    }
    $portable = Join-Path $ProductRoot "app\cw.exe"
    if (Test-Path $portable) { $candidates.Add(@($portable)) }

    foreach ($c in $candidates) {
        $exe = $c[0]
        $pre = @($c | Select-Object -Skip 1)
        & $exe @pre version *> $null
        if ($LASTEXITCODE -eq 0) { return , $c }
    }
    return $null
}

function Invoke-Cw($Cw, [string[]] $Arguments) {
    $exe = $Cw[0]
    $pre = @($Cw | Select-Object -Skip 1)
    & $exe @pre @Arguments 2>&1 | ForEach-Object { Write-Host "    $_" }
    return $LASTEXITCODE
}

function Stop-CmdWardenProcesses {
    foreach ($name in "CmdWarden.ApprovalGate", "CmdWarden.SecretsManager", "CmdWarden.Agent", "cw", "cmdwarden") {
        Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
            Write-Ok "Stopped $($_.Name) (pid $($_.Id))"
        }
    }
    # The Session Agent runs as dotnet.exe CmdWarden.Agent.dll.
    Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -match 'CmdWarden\.Agent|[/\\]cmdwarden[/\\]' } |
        ForEach-Object {
            Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
            Write-Ok "Stopped Session Agent (pid $($_.ProcessId))"
        }
}

function Remove-PathEntries([string] $Scope) {
    $current = [Environment]::GetEnvironmentVariable("Path", $Scope)
    if (-not $current) { return @() }
    $parts = $current -split ';' | Where-Object { $_ }
    $ours = @($parts | Where-Object { $_ -match '[\\/]CmdWarden([\\/]|$)' } | Select-Object -Unique)
    if ($ours.Count -eq 0) { return @() }
    if ($Scope -eq "User") {
        $keep = $parts | Where-Object { $_ -notmatch '[\\/]CmdWarden([\\/]|$)' }
        [Environment]::SetEnvironmentVariable("Path", ($keep -join ';'), "User")
    }
    return $ours
}

# Used only when no cw can unharden: take out the CmdWarden helper so git and docker work again.
function Clear-GitHelper {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { return }
    $lines = & git config --global --get-regexp '^credential\..*helper$' 2>$null
    foreach ($line in @($lines)) {
        if ($line -notmatch 'cmdwarden') { continue }
        $key = ($line -split ' ', 2)[0]
        & git config --global --unset-all $key 'cmdwarden' 2>$null
        Write-Warn "Removed the CmdWarden git credential helper ($key). Sign in to git again if a push asks."
    }
}

function Clear-DockerHelper {
    $config = Join-Path $env:USERPROFILE ".docker\config.json"
    if (-not (Test-Path $config)) { return }
    try {
        $json = Get-Content $config -Raw | ConvertFrom-Json
        if ($json.PSObject.Properties.Name -contains "credsStore" -and $json.credsStore -eq "cmdwarden") {
            $json.PSObject.Properties.Remove("credsStore")
            $json | ConvertTo-Json -Depth 20 | Set-Content -Path $config -Encoding utf8
            Write-Warn "Removed credsStore=cmdwarden from $config. Run docker login again."
        }
    }
    catch {
        Write-Warn "Could not read $config : $_"
    }
}

# --- main ---
Write-Host "CmdWarden uninstaller" -ForegroundColor Cyan
Write-Host "Product data: $ProductRoot"
if (-not $Quiet) {
    if (-not (Confirm-Step "Remove CmdWarden from this account?" $true)) {
        Write-Host "Nothing changed."
        exit 0
    }
}

Write-Step "Unharden gh, git, az, docker"
$cw = Find-Cw
if ($cw) {
    Write-Ok "Using: $($cw -join ' ')"
    foreach ($tool in "gh", "git", "az", "docker") {
        if ((Invoke-Cw $cw @("unharden", $tool)) -ne 0) {
            Write-Warn "cw unharden $tool failed. See the lines above."
        }
    }
}
else {
    Write-Warn "No working cw found. Unharden runs as a direct clean-up."
}

# A hook left in the harness config would call a cw that is gone, on every tool call.
Write-Step "Remove the Claude Code and Cursor hooks"
if ($cw) {
    foreach ($command in "leak-guard", "hook") {
        foreach ($harness in "claude", "cursor") {
            Invoke-Cw $cw @($command, "uninstall", $harness) | Out-Null
        }
    }
}
else {
    Write-Warn "No working cw found. Remove the entries with 'leak-guard' or 'hook check' from ~\.claude\settings.json and ~\.cursor\hooks.json."
}
Clear-GitHelper
Clear-DockerHelper

Write-Step "Stop CmdWarden processes"
if ($cw) { Invoke-Cw $cw @("agent", "stop") | Out-Null }
Stop-CmdWardenProcesses

Write-Step "Remove shortcuts"
$links = @(
    (Join-Path ([Environment]::GetFolderPath("Programs")) "CmdWarden Vault.lnk"),
    (Join-Path ([Environment]::GetFolderPath("DesktopDirectory")) "CmdWarden Vault.lnk")
)
foreach ($lnk in $links) {
    if (Test-Path $lnk) {
        Remove-Item -Force $lnk -ErrorAction SilentlyContinue
        Write-Ok "Removed $lnk"
    }
}

Write-Step "Uninstall the dotnet tool"
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    & dotnet tool uninstall -g CmdWarden *> $null
    if ($LASTEXITCODE -eq 0) { Write-Ok "dotnet tool uninstall -g CmdWarden" }
}
foreach ($path in $ToolStore, (Join-Path $ToolsDir "cw.exe"), (Join-Path $ToolsDir "cmdwarden.exe")) {
    for ($try = 0; $try -lt 5 -and (Test-Path $path); $try++) {
        Remove-Item -Recurse -Force $path -ErrorAction SilentlyContinue
        if (Test-Path $path) { Start-Sleep -Milliseconds 500; Stop-CmdWardenProcesses }
    }
    if (Test-Path $path) { Write-Warn "Could not delete $path. Close all terminals, then delete it." }
}
if (-not (Test-Path $ToolStore)) { Write-Ok "Tool store is clear." }

Write-Step "Remove CmdWarden folders from PATH"
foreach ($entry in (Remove-PathEntries "User")) { Write-Ok "User PATH: removed $entry" }
$machine = @(Remove-PathEntries "Machine")
if ($machine.Count -gt 0) {
    $list = $machine -join ', '
    if (-not $Quiet -and (Confirm-Step "The machine PATH has $list. Remove it (one admin prompt)?" $true)) {
        $cmd = "`$p = [Environment]::GetEnvironmentVariable('Path','Machine') -split ';' | Where-Object { `$_ -and `$_ -notmatch '[\\/]CmdWarden([\\/]|$)' }; [Environment]::SetEnvironmentVariable('Path', (`$p -join ';'), 'Machine')"
        try {
            Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList "-NoProfile", "-Command", $cmd
            Write-Ok "Machine PATH: removed $list"
        }
        catch {
            Write-Warn "Machine PATH still has $list. Remove it in System Properties > Environment Variables."
        }
    }
    else {
        Write-Warn "Machine PATH still has $list. Remove it in System Properties > Environment Variables."
    }
}

Write-Step "Local data"
if ($KeepData) {
    Write-Ok "Kept $ProductRoot (-KeepData)."
}
elseif (Test-Path $ProductRoot) {
    Remove-Item -Recurse -Force $ProductRoot -ErrorAction SilentlyContinue
    if (Test-Path $ProductRoot) { Write-Warn "Could not delete all of $ProductRoot. Delete it after a restart." }
    else { Write-Ok "Deleted $ProductRoot" }
}
else {
    Write-Ok "No local data."
}

Write-Step "Saved secrets"
$targets = @(cmdkey /list 2>$null |
    Select-String -Pattern 'target=(CmdWarden/[^\s]+)' |
    ForEach-Object { $_.Matches[0].Groups[1].Value })
if ($targets.Count -eq 0) {
    Write-Ok "None in Credential Manager."
}
elseif ($RemoveSecrets -or (Confirm-Step "Delete $($targets.Count) saved CmdWarden secrets from Credential Manager?" $false)) {
    foreach ($t in $targets) {
        cmdkey /delete:$t *> $null
        Write-Ok "Deleted $t"
    }
}
else {
    Write-Ok "Kept $($targets.Count) secrets in Credential Manager under CmdWarden/."
}

Write-Step "Windows Settings > Apps entry"
if (Test-Path $UninstallKey) {
    Remove-Item -Recurse -Force $UninstallKey -ErrorAction SilentlyContinue
    Write-Ok "Removed."
}
else {
    Write-Ok "None."
}
$copy = Join-Path $ProductRoot "uninstall"
if ($KeepData -and (Test-Path $copy)) { Remove-Item -Recurse -Force $copy -ErrorAction SilentlyContinue }

Write-Host ""
if ($script:Warnings.Count -eq 0) {
    Write-Host "CmdWarden is removed. Open a new terminal." -ForegroundColor Green
}
else {
    Write-Host "CmdWarden is removed. Check these items ($($script:Warnings.Count)):" -ForegroundColor Yellow
    foreach ($w in $script:Warnings) { Write-Host "  - $w" -ForegroundColor Yellow }
}
if (-not $Quiet) { Read-Host "Press Enter to close" | Out-Null }
exit 0
