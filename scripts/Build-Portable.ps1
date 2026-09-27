#Requires -Version 7
<#
.SYNOPSIS
  Build the portable CmdWarden folder (#64): every app plus one private .NET runtime. No .NET install needed.

.DESCRIPTION
  Each app publishes framework-dependent, and its apphost looks only in a relative runtime folder
  (AppHostDotNetSearch=AppRelative). All apps share one runtime\ at the root of the folder:

    cw.exe, cmdwarden.exe      -> runtime\
    agent\                     -> ..\runtime\     (ASP.NET Core)
    agent\approval-gate\       -> ..\..\runtime\  (WPF)
    secrets-manager\           -> ..\runtime\     (WPF)
    shim-payload\              -> ..\runtime\     (cw harden copies the shims to %LOCALAPPDATA%\CmdWarden\shims
                                                   and the base runtime to %LOCALAPPDATA%\CmdWarden\runtime)
    try-agent\                 -> ..\runtime\
    runtime\                   host\fxr and shared\ of Microsoft.NETCore.App, AspNetCore.App, WindowsDesktop.App

  The check at the end runs cw.exe version with no dotnet on PATH. The apphost cannot fall back to an
  installed .NET, so a pass proves the private runtime works.

.EXAMPLE
  ./scripts/Build-Portable.ps1 -Version 0.8.0 -Out artifacts/portable
#>
[CmdletBinding()]
param(
    [string] $Version = "0.0.0-dev",
    [string] $Out = "artifacts/portable",
    [string] $Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$Out = [System.IO.Path]::GetFullPath((Join-Path $repo $Out))
if (Test-Path $Out) { Remove-Item -Recurse -Force $Out }
New-Item -ItemType Directory -Force $Out | Out-Null

function Publish([string] $Project, [string] $Folder, [string] $Runtime) {
    $dir = if ($Folder) { Join-Path $Out $Folder } else { $Out }
    Write-Host "==> $Project -> $(if ($Folder) { $Folder } else { '.' }) (runtime: $Runtime)"
    # win-x64 only: without a runtime id, publish adds the native files of Linux and macOS too.
    & dotnet publish (Join-Path $repo "src/$Project/$Project.csproj") -c $Configuration -o $dir --nologo -v quiet `
        -r win-x64 --self-contained false `
        -p:Version=$Version -p:PackageVersion=$Version -p:CwSkipBundle=true `
        -p:AppHostDotNetSearch=AppRelative -p:AppHostRelativeDotNet=$Runtime
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish $Project failed (exit $LASTEXITCODE)." }
}

Publish "CmdWarden.Cli" "" "runtime"
Copy-Item (Join-Path $Out "cw.exe") (Join-Path $Out "cmdwarden.exe")
Publish "CmdWarden.Agent" "agent" "..\runtime"
Publish "CmdWarden.ApprovalGate" "agent\approval-gate" "..\..\runtime"
Publish "CmdWarden.SecretsManager" "secrets-manager" "..\runtime"
foreach ($p in "CmdWarden.Shim.Gh", "CmdWarden.Shim.Git", "CmdWarden.Shim.Az", "CmdWarden.Shim.Docker", "CmdWarden.Shim.Pack",
    "CmdWarden.Helper.Git", "CmdWarden.Helper.Docker") {
    Publish $p "shim-payload" "..\runtime"
}
Publish "CmdWarden.TryAgent" "try-agent" "..\runtime"
Get-ChildItem $Out -Recurse -Filter *.pdb | Remove-Item -Force

# The runtime of the SDK that built the apps: the newest 10.x of each shared framework.
$dotnetRoot = if ($env:DOTNET_ROOT) { $env:DOTNET_ROOT } else { Split-Path -Parent (Get-Command dotnet).Source }
function Newest([string] $Dir) {
    Get-ChildItem $Dir -Directory | Where-Object { $_.Name -match '^10\.' } |
        Sort-Object { [version]($_.Name -replace '-.*$', '') } | Select-Object -Last 1
}
$runtime = Join-Path $Out "runtime"
$fxr = Newest (Join-Path $dotnetRoot "host\fxr")
New-Item -ItemType Directory -Force (Join-Path $runtime "host\fxr") | Out-Null
Copy-Item -Recurse $fxr.FullName (Join-Path $runtime "host\fxr\$($fxr.Name)")
foreach ($framework in "Microsoft.NETCore.App", "Microsoft.AspNetCore.App", "Microsoft.WindowsDesktop.App") {
    $frameworkDir = Newest (Join-Path $dotnetRoot "shared\$framework")
    if (-not $frameworkDir) { throw "No 10.x $framework under $dotnetRoot." }
    New-Item -ItemType Directory -Force (Join-Path $runtime "shared\$framework") | Out-Null
    Copy-Item -Recurse $frameworkDir.FullName (Join-Path $runtime "shared\$framework\$($frameworkDir.Name)")
    Write-Host "    runtime: $framework $($frameworkDir.Name)"
}
foreach ($notice in "LICENSE.txt", "ThirdPartyNotices.txt") {
    if (Test-Path (Join-Path $dotnetRoot $notice)) { Copy-Item (Join-Path $dotnetRoot $notice) $runtime }
}

$required = @(
    "cw.exe", "cmdwarden.exe", "uninstall\Uninstall-CmdWarden.ps1",
    "agent\CmdWarden.Agent.exe", "agent\approval-gate\CmdWarden.ApprovalGate.exe",
    "secrets-manager\CmdWarden.SecretsManager.exe", "try-agent\cw-try-agent.exe",
    "shim-payload\gh.exe", "shim-payload\git.exe", "shim-payload\az.exe", "shim-payload\docker.exe",
    "shim-payload\cw-pack-shim.exe", "shim-payload\git-credential-cmdwarden.exe", "shim-payload\docker-credential-cmdwarden.exe"
)
foreach ($rel in $required) {
    if (-not (Test-Path (Join-Path $Out $rel))) { throw "Missing in the portable folder: $rel" }
}

# Only the private runtime: no dotnet on PATH, no DOTNET_ROOT.
$psi = [System.Diagnostics.ProcessStartInfo]::new((Join-Path $Out "cw.exe"), "version")
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
foreach ($name in @($psi.Environment.Keys | Where-Object { $_ -match '^(PATH|DOTNET_ROOT.*)$' })) { $psi.Environment.Remove($name) | Out-Null }
$psi.Environment["PATH"] = "$env:SystemRoot\System32"
$check = [System.Diagnostics.Process]::Start($psi)
$text = $check.StandardOutput.ReadToEnd() + $check.StandardError.ReadToEnd()
$check.WaitForExit()
if ($check.ExitCode -ne 0 -or $text -notmatch "CmdWarden") { throw "cw.exe version failed on the private runtime: $text" }
$size = [math]::Round(((Get-ChildItem $Out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "Portable folder: $Out ($size MB). $($text.Trim() -split "`n" | Select-Object -First 1)"
