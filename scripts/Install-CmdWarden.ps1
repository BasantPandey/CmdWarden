#Requires -Version 5.1
<#
.SYNOPSIS
  Install CmdWarden on Windows from a GitHub Release (or a local nupkg).

.DESCRIPTION
  Installs CmdWarden as a global dotnet tool (cw / cmdwarden). Optionally installs the portable zip.

  Package source, in this order:
    1. -PackagePath.
    2. A CmdWarden.<version>.nupkg next to this script (the setup zip has one). No download.
    3. The GitHub Release. Private repos: use an authenticated `gh` CLI, or set GH_TOKEN / GITHUB_TOKEN.

  When the .NET 10 SDK is missing, the script offers to install it with winget.
  The script registers CmdWarden in Windows Settings > Apps. Uninstall runs Uninstall-CmdWarden.ps1.

.PARAMETER Version
  Package version without "v" (e.g. 0.1.0). Default: latest GitHub Release.

.PARAMETER Tag
  Release tag (e.g. v0.1.0). Overrides Version when resolving the release.

.PARAMETER Repo
  GitHub owner/name. Default: BasantPandey/CmdWarden

.PARAMETER Mode
  tool  - global dotnet tool (default, recommended)
  zip   - extract portable zip to InstallDir

.PARAMETER PackagePath
  Install from a local .nupkg instead of downloading.

.PARAMETER InstallDir
  For Mode=zip: extract directory. Default: $env:LOCALAPPDATA\CmdWarden\app

.PARAMETER SkipDoctor
  Do not run `cw doctor` after install.

.PARAMETER Force
  Uninstall existing global tool before install.

.PARAMETER Desktop
  Also create a Desktop icon for CmdWarden Vault (Start Menu entry is always created).

.PARAMETER Yes
  Answer yes to every question (install the .NET SDK with winget when it is missing).

.EXAMPLE
  # Latest release as global tool
  .\scripts\Install-CmdWarden.ps1

.EXAMPLE
  # Specific version
  .\scripts\Install-CmdWarden.ps1 -Version 0.1.0

.EXAMPLE
  # Portable zip
  .\scripts\Install-CmdWarden.ps1 -Mode zip -InstallDir "$env:LOCALAPPDATA\CmdWarden\app"

.EXAMPLE
  # Local nupkg
  .\scripts\Install-CmdWarden.ps1 -PackagePath C:\packages\CmdWarden.0.1.0.nupkg

.EXAMPLE
  # Latest release plus a Desktop icon for CmdWarden Vault
  .\scripts\Install-CmdWarden.ps1 -Desktop
#>
[CmdletBinding()]
param(
    [string] $Version,
    [string] $Tag,
    [string] $Repo = "BasantPandey/CmdWarden",
    [ValidateSet("tool", "zip")]
    [string] $Mode = "tool",
    [string] $PackagePath,
    [string] $InstallDir = (Join-Path $env:LOCALAPPDATA "CmdWarden\app"),
    [switch] $SkipDoctor,
    [switch] $Force,
    [switch] $Desktop,
    [switch] $Yes
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step([string] $Message) {
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Write-Ok([string] $Message) {
    Write-Host "    $Message" -ForegroundColor Green
}

function Write-Warn([string] $Message) {
    Write-Host "    $Message" -ForegroundColor Yellow
}

function Assert-Windows {
    if (-not ($env:OS -eq "Windows_NT" -or $IsWindows -eq $true)) {
        throw "CmdWarden install is supported on Windows only."
    }
}

function Test-DotNet10 {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { return $false }
    if ($Mode -eq "zip") {
        return [bool](& dotnet --list-runtimes 2>$null | Select-String '^Microsoft\.WindowsDesktop\.App 10\.')
    }
    return [bool](& dotnet --list-sdks 2>$null | Select-String '^10\.')
}

function Confirm-Yes([string] $Question) {
    if ($Yes) { return $true }
    try { $answer = Read-Host "$Question [Y/n]" } catch { return $false }
    return [string]::IsNullOrWhiteSpace($answer) -or $answer.Trim().ToLowerInvariant().StartsWith("y")
}

function Assert-DotNet {
    if (-not (Test-DotNet10)) {
        $winget = Get-Command winget -ErrorAction SilentlyContinue
        if ($winget -and (Confirm-Yes "The .NET 10 SDK is missing. Install it now with winget?")) {
            Write-Step "Installing the .NET 10 SDK with winget"
            & winget install --id Microsoft.DotNet.SDK.10 --exact --silent --accept-source-agreements --accept-package-agreements
            $dotnetDir = Join-Path $env:ProgramFiles "dotnet"
            if ($env:Path -notlike "*$dotnetDir*") { $env:Path = "$dotnetDir;$env:Path" }
        }
    }
    if (-not (Test-DotNet10)) {
        throw @"
The .NET 10 SDK was not found.

Install it, then run this installer again:
  winget install --id Microsoft.DotNet.SDK.10 --exact
  or https://dotnet.microsoft.com/download/dotnet/10.0
"@
    }
    $dotnet = Get-Command dotnet
    $ver = & dotnet --version 2>$null
    Write-Ok "dotnet found: $ver ($($dotnet.Source))"
}

function Get-DotNetToolsPath {
    Join-Path $env:USERPROFILE ".dotnet\tools"
}

function Ensure-DotNetToolsOnPath {
    $tools = Get-DotNetToolsPath
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    if (-not $userPath) { $userPath = "" }
    $parts = $userPath -split ';' | Where-Object { $_ }
    $onUserPath = $parts | Where-Object { $_.TrimEnd('\') -ieq $tools.TrimEnd('\') }
    if (-not $onUserPath) {
        Write-Warn "Adding $tools to user PATH (new terminals will pick this up)."
        $newPath = if ([string]::IsNullOrWhiteSpace($userPath)) { $tools } else { "$userPath;$tools" }
        [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
    }
    if ($env:Path -notlike "*$tools*") {
        $env:Path = "$tools;$env:Path"
        Write-Ok "Session PATH updated so cw is available immediately."
    }
}

function Test-GhAvailable {
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if (-not $gh) { return $false }
    & gh auth status 2>$null | Out-Null
    return ($LASTEXITCODE -eq 0)
}

function Get-LatestReleaseTag {
    param([string] $Repository)

    if (Test-GhAvailable) {
        $tag = & gh release view --repo $Repository --json tagName --jq .tagName 2>$null
        if ($LASTEXITCODE -eq 0 -and $tag) { return $tag.Trim() }
    }

    $token = $env:GH_TOKEN
    if (-not $token) { $token = $env:GITHUB_TOKEN }
    $headers = @{
        "User-Agent" = "CmdWarden-Install"
        "Accept"     = "application/vnd.github+json"
    }
    if ($token) { $headers["Authorization"] = "Bearer $token" }

    $uri = "https://api.github.com/repos/$Repository/releases/latest"
    try {
        $rel = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get
        if ($rel.tag_name) { return [string]$rel.tag_name }
    }
    catch {
        throw @"
Could not resolve latest GitHub Release for $Repository.

Options:
  1. Install and auth GitHub CLI:  winget install GitHub.cli ; gh auth login
  2. Set GH_TOKEN or GITHUB_TOKEN with repo read access
  3. Pass -Version 0.1.0 (or -Tag v0.1.0) explicitly
  4. Pass -PackagePath path\to\CmdWarden.x.y.z.nupkg

Error: $_
"@
    }
    throw "No latest release found for $Repository."
}

function Resolve-VersionFromTag([string] $ReleaseTag) {
    if ($ReleaseTag -match '^v(.+)$') { return $Matches[1] }
    return $ReleaseTag
}

function Download-ReleaseAsset {
    param(
        [string] $Repository,
        [string] $ReleaseTag,
        [string] $AssetName,
        [string] $OutFile
    )

    $dir = Split-Path -Parent $OutFile
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    if (Test-GhAvailable) {
        Write-Ok "Downloading via gh: $AssetName ($ReleaseTag)"
        & gh release download $ReleaseTag --repo $Repository --pattern $AssetName --dir $dir --clobber
        if ($LASTEXITCODE -ne 0) {
            throw "gh release download failed for $AssetName (tag $ReleaseTag)."
        }
        $found = Join-Path $dir $AssetName
        if (-not (Test-Path $found)) {
            throw "Download finished but file missing: $found"
        }
        if ((Resolve-Path $found).Path -ne (Resolve-Path $OutFile).Path) {
            Move-Item -Force $found $OutFile
        }
        return
    }

    $token = $env:GH_TOKEN
    if (-not $token) { $token = $env:GITHUB_TOKEN }
    $headers = @{
        "User-Agent" = "CmdWarden-Install"
        "Accept"     = "application/vnd.github+json"
    }
    if ($token) { $headers["Authorization"] = "Bearer $token" }

    $relUri = "https://api.github.com/repos/$Repository/releases/tags/$ReleaseTag"
    $rel = Invoke-RestMethod -Uri $relUri -Headers $headers -Method Get
    $asset = $rel.assets | Where-Object { $_.name -eq $AssetName } | Select-Object -First 1
    if (-not $asset) {
        $names = ($rel.assets | ForEach-Object { $_.name }) -join ", "
        throw "Asset '$AssetName' not found on $ReleaseTag. Available: $names"
    }

    # Private repos need Accept: application/octet-stream on the asset API URL
    $downloadHeaders = @{
        "User-Agent" = "CmdWarden-Install"
        "Accept"     = "application/octet-stream"
    }
    if ($token) { $downloadHeaders["Authorization"] = "Bearer $token" }

    Write-Ok "Downloading via API: $AssetName"
    Invoke-WebRequest -Uri $asset.url -Headers $downloadHeaders -OutFile $OutFile
}

function Stop-CmdWardenProcesses {
    foreach ($name in @("cw", "cmdwarden", "CmdWarden.Agent", "CmdWarden.ApprovalGate", "CmdWarden.SecretsManager")) {
        Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
            Write-Warn "Stopping process $($_.Name) (pid $($_.Id)) so the tool store can update"
            try { $_.CloseMainWindow() | Out-Null } catch { }
            Start-Sleep -Milliseconds 200
            try { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue } catch { }
        }
    }

    # Session Agent is usually hosted as dotnet.exe + CmdWarden.Agent.dll (name is not CmdWarden.Agent).
    Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -match 'CmdWarden\.Agent|[/\\]cmdwarden[/\\]' } |
        ForEach-Object {
            Write-Warn "Stopping hosted agent/dotnet (pid $($_.ProcessId)) so the tool store can update"
            try { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue } catch { }
        }
}

function Clear-CmdWardenToolStore {
    $store = Join-Path $env:USERPROFILE ".dotnet\tools\.store\cmdwarden"
    if (Test-Path $store) {
        Write-Warn "Clearing tool store: $store"
        Remove-Item -Recurse -Force $store -ErrorAction SilentlyContinue
    }
    Remove-Item -Force (Join-Path (Get-DotNetToolsPath) "cw.exe") -ErrorAction SilentlyContinue
    Remove-Item -Force (Join-Path (Get-DotNetToolsPath) "cmdwarden.exe") -ErrorAction SilentlyContinue
}

function Install-AsDotNetTool {
    param(
        [string] $NupkgPath,
        [string] $PackageVersion,
        [switch] $ForceReinstall
    )

    if (-not (Test-Path $NupkgPath)) {
        throw "nupkg not found: $NupkgPath"
    }

    $sourceDir = Split-Path -Parent (Resolve-Path $NupkgPath)
    Ensure-DotNetToolsOnPath
    Stop-CmdWardenProcesses

    # Native dotnet stderr can become terminating errors under $ErrorActionPreference Stop.
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"

    $existing = $false
    $listOut = cmd /c "dotnet tool list -g 2>&1"
    if ($listOut -match '(?im)cmdwarden') { $existing = $true }
    if ($listOut -match '(?im)project\.assets\.json|Failed to read NuGet assets') {
        Write-Warn "dotnet tool store looks corrupt (missing project.assets.json); will wipe and reinstall."
        $existing = $true
    }
    $store = Join-Path $env:USERPROFILE ".dotnet\tools\.store\cmdwarden"
    if (-not $existing -and (Test-Path $store)) { $existing = $true }
    # Half-written store: folder exists but assets file is gone (common after interrupted install / file lock).
    if ((Test-Path $store) -and -not (Test-Path (Join-Path $store "0.1.0\project.assets.json")) `
        -and -not (Get-ChildItem $store -Filter project.assets.json -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1)) {
        Write-Warn "Corrupt CmdWarden tool store detected (no project.assets.json)."
        $existing = $true
    }

    if ($existing -or $ForceReinstall) {
        Write-Step "Removing previous CmdWarden tool install (if any)"
        Stop-CmdWardenProcesses
        Start-Sleep -Seconds 1
        $prevLg = Join-Path (Get-DotNetToolsPath) "cw.exe"
        if (Test-Path $prevLg) {
            cmd /c "`"$prevLg`" shortcut remove >nul 2>&1" | Out-Null
        }
        $vaultLnk = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\CmdWarden Vault.lnk"
        Remove-Item -Force $vaultLnk -ErrorAction SilentlyContinue
        cmd /c "dotnet tool uninstall -g CmdWarden >nul 2>&1" | Out-Null
        Clear-CmdWardenToolStore
    }

    Write-Step "Installing global tool CmdWarden $PackageVersion"
    cmd /c "dotnet tool install -g CmdWarden --add-source `"$sourceDir`" --version $PackageVersion"
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        Write-Warn "install failed; retry after stopping processes and clearing store"
        Stop-CmdWardenProcesses
        Start-Sleep -Seconds 1
        cmd /c "dotnet tool uninstall -g CmdWarden >nul 2>&1" | Out-Null
        Clear-CmdWardenToolStore
        cmd /c "dotnet tool install -g CmdWarden --add-source `"$sourceDir`" --version $PackageVersion"
        $code = $LASTEXITCODE
    }

    $ErrorActionPreference = $prevEap

    if ($code -ne 0) {
        throw @"
dotnet tool install/update failed (exit $code).

Common cause: Session Agent still running as dotnet.exe and locking the tool store.

  1. Close cw / CmdWarden Vault / Approval Gate windows
  2. Stop hosted agents:
       Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
         Where-Object { `$_.CommandLine -match 'CmdWarden' } |
         ForEach-Object { Stop-Process -Id `$_.ProcessId -Force }
  3. Wipe store + shims, then reinstall:
       Remove-Item -Recurse -Force `$env:USERPROFILE\.dotnet\tools\.store\cmdwarden -ErrorAction SilentlyContinue
       Remove-Item -Force `$env:USERPROFILE\.dotnet\tools\cw.exe, `$env:USERPROFILE\.dotnet\tools\cmdwarden.exe -ErrorAction SilentlyContinue
       .\scripts\Install-CmdWarden.ps1 -PackagePath <your.nupkg> -Version $PackageVersion -Force
"@
    }

    $cw = Join-Path (Get-DotNetToolsPath) "cw.exe"
    if (-not (Test-Path $cw)) {
        throw "Install reported success but cw.exe not found at $cw"
    }
    Write-Ok "Installed: $cw"
}

function Install-FromZip {
    param(
        [string] $ZipPath,
        [string] $Destination
    )

    if (-not (Test-Path $ZipPath)) {
        throw "zip not found: $ZipPath"
    }
    if (Test-Path $Destination) {
        Write-Warn "Removing existing $Destination"
        Remove-Item -Recurse -Force $Destination
    }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null

    Write-Step "Extracting portable package to $Destination"
    Expand-Archive -Path $ZipPath -DestinationPath $Destination -Force

    $cw = Join-Path $Destination "cw.exe"
    if (-not (Test-Path $cw)) {
        # sometimes zip has a single root folder
        $nested = Get-ChildItem $Destination -Directory | Select-Object -First 1
        if ($nested) {
            $cw = Join-Path $nested.FullName "cw.exe"
        }
    }
    if (-not (Test-Path $cw)) {
        throw "cw.exe not found after extract under $Destination"
    }

    Write-Ok "Portable cw: $cw"
    Write-Warn "Add this folder to PATH if you want 'cw' without the full path:"
    Write-Host "    $($cw | Split-Path -Parent)" -ForegroundColor Yellow
    return $cw
}

function Invoke-Verify {
    param(
        [string] $CwCommand = "cw",
        [switch] $SkipDoctor
    )

    Write-Step "Verify"
    & $CwCommand version
    if ($LASTEXITCODE -ne 0) {
        throw "'$CwCommand version' failed."
    }
    if (-not $SkipDoctor) {
        & $CwCommand doctor
    }
}

function Register-Uninstaller {
    param([string] $PackageVersion, [string] $AppDir)

    Write-Step "Windows Settings > Apps entry"
    $source = Join-Path $PSScriptRoot "Uninstall-CmdWarden.ps1"
    if (-not (Test-Path $source)) {
        Write-Warn "Uninstall-CmdWarden.ps1 is not next to this script. No Apps entry."
        return
    }
    $productRoot = if ($env:CW_PRODUCT_ROOT) { $env:CW_PRODUCT_ROOT } else { Join-Path $env:LOCALAPPDATA "CmdWarden" }
    $dir = Join-Path $productRoot "uninstall"
    New-Item -ItemType Directory -Force $dir | Out-Null
    $script = Join-Path $dir "Uninstall-CmdWarden.ps1"
    Copy-Item -Force $source $script

    $powershell = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
    $run = "`"$powershell`" -NoProfile -ExecutionPolicy Bypass -File `"$script`""
    $icon = Get-ChildItem $AppDir -Recurse -Filter CmdWarden.SecretsManager.exe -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
    $sizeKb = [int]((Get-ChildItem $AppDir -Recurse -File -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum / 1KB)

    $key = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CmdWarden"
    New-Item -Path $key -Force | Out-Null
    $values = @{
        DisplayName          = "CmdWarden"
        DisplayVersion       = $PackageVersion
        Publisher            = "CmdWarden"
        URLInfoAbout         = "https://github.com/$Repo"
        InstallLocation      = $AppDir
        UninstallString      = $run
        QuietUninstallString = "$run -Quiet"
        NoModify             = 1
        NoRepair             = 1
        EstimatedSize        = $sizeKb
    }
    if ($icon) { $values.DisplayIcon = $icon }
    foreach ($name in $values.Keys) {
        $type = if ($values[$name] -is [int]) { "DWord" } else { "String" }
        New-ItemProperty -Path $key -Name $name -Value $values[$name] -PropertyType $type -Force | Out-Null
    }
    Write-Ok "Registered. Uninstall: Settings > Apps > CmdWarden, or run $script"
}

function Install-VaultShortcut {
    param([string] $CwCommand = "cw", [switch] $WithDesktop)

    $shortcutArgs = @("shortcut", "install")
    if ($WithDesktop) { $shortcutArgs += "--desktop" }
    $where = if ($WithDesktop) { "Start Menu + Desktop" } else { "Start Menu" }
    Write-Step "$where shortcut (CmdWarden Vault)"
    & $CwCommand @shortcutArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Warn "Could not create the shortcut (vault UI binary may be missing). Later: $CwCommand $($shortcutArgs -join ' ')"
        return
    }
    Write-Ok "Shortcut installed (or updated)."
}

# --- main ---
Assert-Windows
Write-Step "CmdWarden Windows installer"
Write-Ok "Repo: $Repo  Mode: $Mode"

Assert-DotNet

$work = Join-Path $env:TEMP ("CmdWarden-install-" + [guid]::NewGuid().ToString("n"))
New-Item -ItemType Directory -Path $work -Force | Out-Null

try {
    if ($Mode -eq "tool") {
        $nupkg = $null
        $pkgVersion = $Version

        if ($PackagePath) {
            if (-not (Test-Path $PackagePath)) {
                throw "PackagePath not found: $PackagePath"
            }
            $nupkg = (Resolve-Path $PackagePath).Path
            if (-not $pkgVersion) {
                if ($nupkg -match 'CmdWarden\.([0-9][^\\/]+)\.nupkg$') {
                    $pkgVersion = $Matches[1]
                }
                else {
                    throw "Could not parse version from nupkg name. Pass -Version."
                }
            }
            Write-Ok "Using local nupkg: $nupkg"
        }
        elseif (-not $Version -and -not $Tag -and ($bundled = Get-ChildItem $PSScriptRoot -Filter "CmdWarden.*.nupkg" -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match '^CmdWarden\.([0-9][^\\/]*)\.nupkg$' } |
                Sort-Object LastWriteTime -Descending | Select-Object -First 1)) {
            $nupkg = $bundled.FullName
            $null = $bundled.Name -match '^CmdWarden\.([0-9][^\\/]*)\.nupkg$'
            $pkgVersion = $Matches[1]
            Write-Ok "Using the package next to this script: $nupkg"
        }
        else {
            $releaseTag = $Tag
            if (-not $releaseTag) {
                if ($Version) {
                    $releaseTag = if ($Version.StartsWith("v")) { $Version } else { "v$Version" }
                }
                else {
                    Write-Step "Resolving latest GitHub Release"
                    $releaseTag = Get-LatestReleaseTag -Repository $Repo
                }
            }
            $pkgVersion = Resolve-VersionFromTag $releaseTag
            Write-Ok "Release tag: $releaseTag  package version: $pkgVersion"

            $asset = "CmdWarden.$pkgVersion.nupkg"
            $nupkg = Join-Path $work $asset
            Write-Step "Download $asset"
            Download-ReleaseAsset -Repository $Repo -ReleaseTag $releaseTag -AssetName $asset -OutFile $nupkg
            Write-Ok "Saved $nupkg"
        }

        Install-AsDotNetTool -NupkgPath $nupkg -PackageVersion $pkgVersion -ForceReinstall:$Force
        Install-VaultShortcut -CwCommand "cw" -WithDesktop:$Desktop
        Register-Uninstaller -PackageVersion $pkgVersion -AppDir (Join-Path (Get-DotNetToolsPath) ".store\cmdwarden")
        Invoke-Verify -CwCommand "cw" -SkipDoctor:$SkipDoctor

        Write-Host ""
        Write-Host "CmdWarden $pkgVersion installed as a global dotnet tool." -ForegroundColor Green
        Write-Host "Next: open a new terminal if 'cw' is not found, then:" -ForegroundColor Green
        Write-Host "  cw doctor" -ForegroundColor Green
        Write-Host "  cw whoami" -ForegroundColor Green
        Write-Host "  cw policy enroll --kind terminal" -ForegroundColor Green
        Write-Host "  cw harden gh" -ForegroundColor Green
        Write-Host "  Start Menu: CmdWarden Vault (Desktop icon: cw shortcut install --desktop)" -ForegroundColor Green
        Write-Host "Uninstall: Settings > Apps > CmdWarden, or uninstall.cmd next to this installer" -ForegroundColor Green
        Write-Host "Docs: docs/install.md and docs/user-guide.md" -ForegroundColor Green
    }
    else {
        # zip mode
        $releaseTag = $Tag
        if (-not $releaseTag) {
            if ($Version) {
                $releaseTag = if ($Version.StartsWith("v")) { $Version } else { "v$Version" }
            }
            else {
                Write-Step "Resolving latest GitHub Release"
                $releaseTag = Get-LatestReleaseTag -Repository $Repo
            }
        }
        $pkgVersion = Resolve-VersionFromTag $releaseTag
        $asset = "CmdWarden.$pkgVersion-win-x64.zip"
        $zip = Join-Path $work $asset
        Write-Step "Download $asset"
        Download-ReleaseAsset -Repository $Repo -ReleaseTag $releaseTag -AssetName $asset -OutFile $zip
        $cw = Install-FromZip -ZipPath $zip -Destination $InstallDir
        Install-VaultShortcut -CwCommand $cw -WithDesktop:$Desktop
        Register-Uninstaller -PackageVersion $pkgVersion -AppDir $InstallDir
        if (-not $SkipDoctor) {
            Write-Step "Verify (portable)"
            & $cw version
            & $cw doctor
        }
        Write-Host ""
        Write-Host "CmdWarden $pkgVersion extracted to $InstallDir" -ForegroundColor Green
        Write-Host "Run: `"$cw`" doctor" -ForegroundColor Green
        Write-Host "Start Menu: CmdWarden Vault (Desktop icon: `"$cw`" shortcut install --desktop)" -ForegroundColor Green
    }
}
finally {
    if (Test-Path $work) {
        Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
    }
}
