#Requires -Version 7
<#
.SYNOPSIS
  Smoke test of the portable CmdWarden folder (#64): it runs with no .NET on PATH.

.DESCRIPTION
  Runs cw version, then cw try against a private Session Agent with a scripted deny gate. That
  starts the agent (ASP.NET Core), the stand-in agent, and cw inject on the private runtime only.
  Nothing touches the real product root, vault, or home: each has a private place.
  With -RequireSigned, each CmdWarden exe must carry a valid signature.

.EXAMPLE
  ./scripts/Test-Portable.ps1 -Folder artifacts/portable
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Folder,
    [switch] $RequireSigned
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$Folder = (Resolve-Path $Folder).Path
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("cw-portable-" + [guid]::NewGuid().ToString("n").Substring(0, 8))
New-Item -ItemType Directory -Force $work, "$work\home" | Out-Null
$pipe = "cmdwarden-smoke-" + [guid]::NewGuid().ToString("n").Substring(0, 8)

function Invoke-Cw([string[]] $Arguments) {
    $psi = [System.Diagnostics.ProcessStartInfo]::new((Join-Path $Folder "cw.exe"))
    foreach ($a in $Arguments) { $psi.ArgumentList.Add($a) }
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($name in @($psi.Environment.Keys | Where-Object { $_ -match '^(PATH|DOTNET_ROOT.*)$' })) { $psi.Environment.Remove($name) | Out-Null }
    $psi.Environment["PATH"] = "$env:SystemRoot\System32"
    $psi.Environment["CW_PIPE_NAME"] = $pipe
    $psi.Environment["CW_PRODUCT_ROOT"] = "$work\product"
    $psi.Environment["CW_POLICY_PATH"] = "$work\product\policy.json"
    $psi.Environment["CW_VAULT_ROOT"] = "CmdWardenSmoke-$pipe/"
    $psi.Environment["CW_HOME"] = "$work\home"
    $psi.Environment["CW_APPROVAL_MODE"] = "deny"
    $p = [System.Diagnostics.Process]::Start($psi)
    $out = $p.StandardOutput.ReadToEndAsync()
    $err = $p.StandardError.ReadToEndAsync()
    if (-not $p.WaitForExit(120000)) { $p.Kill($true); throw "cw $($Arguments -join ' ') did not end in 120 s." }
    [pscustomobject]@{ Exit = $p.ExitCode; Out = $out.Result; Err = $err.Result }
}

try {
    $version = Invoke-Cw @("version")
    if ($version.Exit -ne 0) { throw "cw version failed: $($version.Out)$($version.Err)" }
    Write-Host "ok   cw version: $(($version.Out -split "`n")[0].Trim())"

    $try = Invoke-Cw @("try")
    Write-Host $try.Out
    if ($try.Exit -ne 3 -or $try.Out -notmatch "Blocked\." -or $try.Out -notmatch "CW_TRY_TOKEN") {
        throw "cw try did not block (exit $($try.Exit)): $($try.Err)"
    }
    Write-Host "ok   cw try: the Session Agent ran on the private runtime and the gate blocked the stand-in agent"

    # A shim runs from the product folder, so harden copies the base runtime next to it.
    $harden = Invoke-Cw @("harden", "gh", "--path", "$env:SystemRoot\System32\whoami.exe", "--skip-token", "--skip-path")
    if ($harden.Exit -ne 0) { throw "cw harden gh failed: $($harden.Out)$($harden.Err)" }
    if (-not (Test-Path "$work\product\runtime\shared\Microsoft.NETCore.App")) { throw "harden did not copy the runtime next to the shims." }
    $shim = [System.Diagnostics.ProcessStartInfo]::new("$work\product\shims\gh.exe", "auth status")
    $shim.UseShellExecute = $false
    $shim.RedirectStandardOutput = $true
    $shim.RedirectStandardError = $true
    foreach ($name in @($shim.Environment.Keys | Where-Object { $_ -match '^(PATH|DOTNET_ROOT.*)$' })) { $shim.Environment.Remove($name) | Out-Null }
    $shim.Environment["PATH"] = "$env:SystemRoot\System32"
    $shim.Environment["CW_PIPE_NAME"] = $pipe
    $shim.Environment["CW_PRODUCT_ROOT"] = "$work\product"
    $shim.Environment["CW_POLICY_PATH"] = "$work\product\policy.json"
    $shim.Environment["CW_VAULT_ROOT"] = "CmdWardenSmoke-$pipe/"
    $p = [System.Diagnostics.Process]::Start($shim)
    $shimText = $p.StandardOutput.ReadToEnd() + $p.StandardError.ReadToEnd()
    $p.WaitForExit()
    if ($shimText -notmatch "CmdWarden") { throw "The gh shim did not run on the copied runtime (exit $($p.ExitCode)): $shimText" }
    Write-Host "ok   gh shim: it ran from the product folder on the copied runtime ($(($shimText -split "`n")[0].Trim()))"

    if ($RequireSigned) {
        $unsigned = Get-ChildItem $Folder -Recurse -Include *.exe |
            Where-Object { $_.VersionInfo.CompanyName -eq "CmdWarden" } |
            Where-Object { (Get-AuthenticodeSignature $_.FullName).Status -ne "Valid" }
        if ($unsigned) { throw "Not signed: $($unsigned.FullName -join ', ')" }
        Write-Host "ok   every CmdWarden exe has a valid signature"
    }
}
finally {
    Invoke-Cw @("agent", "stop") | Out-Null
    cmdkey /list | Select-String "CmdWardenSmoke-$pipe/" | ForEach-Object {
        if ($_ -match 'target=(\S+)') { cmdkey "/delete:$($Matches[1])" | Out-Null }
    }
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
