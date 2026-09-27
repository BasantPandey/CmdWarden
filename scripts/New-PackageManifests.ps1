#Requires -Version 7
<#
.SYNOPSIS
  Write the winget and Scoop manifests for one release of the portable zip (#64).

.DESCRIPTION
  winget: a portable zip. ArchiveBinariesDependOnPath puts the install folder on PATH, so cw.exe
  runs from its folder and finds runtime\ next to it (a symlink would not).
  Scoop: the same zip, with cw and cmdwarden on the Scoop shims.

.EXAMPLE
  ./scripts/New-PackageManifests.ps1 -Version 0.8.0 -Zip artifacts/CmdWarden.0.8.0-win-x64.zip -Out artifacts/packages
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Zip,
    [string] $Out = "artifacts/packages",
    [string] $Repo = "BasantPandey/CmdWarden"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$id = "BasantPandey.CmdWarden"
$url = "https://github.com/$Repo/releases/download/v$Version/CmdWarden.$Version-win-x64.zip"
$sha = (Get-FileHash $Zip -Algorithm SHA256).Hash
$winget = Join-Path $Out "winget"
$scoop = Join-Path $Out "scoop"
New-Item -ItemType Directory -Force $winget, $scoop | Out-Null

@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.9.0.schema.json
PackageIdentifier: $id
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.9.0
"@ | Set-Content (Join-Path $winget "$id.yaml") -Encoding utf8NoBOM

@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.9.0.schema.json
PackageIdentifier: $id
PackageVersion: $Version
InstallerType: zip
NestedInstallerType: portable
NestedInstallerFiles:
  - RelativeFilePath: cw.exe
    PortableCommandAlias: cw
  - RelativeFilePath: cmdwarden.exe
    PortableCommandAlias: cmdwarden
ArchiveBinariesDependOnPath: true
InstallModes:
  - silent
UpgradeBehavior: install
Commands:
  - cw
  - cmdwarden
ReleaseDate: $(Get-Date -Format yyyy-MM-dd)
Installers:
  - Architecture: x64
    InstallerUrl: $url
    InstallerSha256: $sha
ManifestType: installer
ManifestVersion: 1.9.0
"@ | Set-Content (Join-Path $winget "$id.installer.yaml") -Encoding utf8NoBOM

@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.1.9.0.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: en-US
Publisher: CmdWarden
PublisherUrl: https://github.com/$Repo
PublisherSupportUrl: https://github.com/$Repo/issues
Author: Basant Pandey
PackageName: CmdWarden
PackageUrl: https://basantpandey.github.io/CmdWarden/
License: MIT
LicenseUrl: https://github.com/$Repo/blob/main/LICENSE
ShortDescription: Make AI coding agents ask before they use your CLI secrets on Windows.
Description: |-
  CmdWarden gates CLI secret use on Windows by tool and launcher. Your terminal keeps working.
  Claude Code, Cursor, and Codex get a policy and an Approval Gate card before they use gh, git,
  az, docker, npm, aws, kubectl, or ssh credentials. It carries its own .NET runtime.
  After install, run: cw setup
Moniker: cmdwarden
Tags:
  - security
  - secrets
  - ai-agents
  - claude-code
  - cursor
  - codex
  - cli
ReleaseNotesUrl: https://github.com/$Repo/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: 1.9.0
"@ | Set-Content (Join-Path $winget "$id.locale.en-US.yaml") -Encoding utf8NoBOM

[ordered]@{
    version     = $Version
    description = "Make AI coding agents ask before they use your CLI secrets on Windows (cw)."
    homepage    = "https://basantpandey.github.io/CmdWarden/"
    license     = "MIT"
    url         = $url
    hash        = $sha.ToLowerInvariant()
    bin         = @("cw.exe", "cmdwarden.exe")
    checkver    = @{ github = "https://github.com/$Repo" }
    autoupdate  = @{ url = "https://github.com/$Repo/releases/download/v`$version/CmdWarden.`$version-win-x64.zip" }
    notes       = @("Run: cw setup")
} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $scoop "cmdwarden.json") -Encoding utf8NoBOM

Write-Host "Manifests for $Version in $Out (sha256 $sha)"
