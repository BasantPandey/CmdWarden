$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

$packageName = 'cmdwarden'
$version = $env:ChocolateyPackageVersion
if (-not $version) { $version = '0.1.0' }

# Optional override: choco install cmdwarden --params "'/Repo:owner/name'"
$repo = 'BasantPandey/CmdWarden'
$pp = Get-PackageParameters
if ($pp.Repo) { $repo = $pp.Repo }

$tag = "v$version"
$nupkgName = "CmdWarden.$version.nupkg"
$downloadDir = Join-Path $env:TEMP "chocolatey\$packageName\$version"
New-Item -ItemType Directory -Force -Path $downloadDir | Out-Null
$nupkgPath = Join-Path $downloadDir $nupkgName

Write-Host "Installing CmdWarden $version as a global dotnet tool..."

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
  throw @"
dotnet was not found on PATH.

Install .NET 10 from https://dotnet.microsoft.com/download then re-run:
  choco install cmdwarden
"@
}

function Get-GitHubReleaseAsset {
  param([string]$Repository, [string]$Tag, [string]$AssetName, [string]$OutFile)

  $gh = Get-Command gh -ErrorAction SilentlyContinue
  if ($gh) {
    & gh auth status 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
      $dir = Split-Path $OutFile -Parent
      & gh release download $Tag --repo $Repository --pattern $AssetName --dir $dir --clobber
      if ($LASTEXITCODE -eq 0 -and (Test-Path (Join-Path $dir $AssetName))) {
        if ((Join-Path $dir $AssetName) -ne $OutFile) {
          Move-Item -Force (Join-Path $dir $AssetName) $OutFile
        }
        return
      }
    }
  }

  $token = $env:GH_TOKEN
  if (-not $token) { $token = $env:GITHUB_TOKEN }
  $apiHeaders = @{
    'User-Agent' = 'CmdWarden-Chocolatey'
    'Accept'     = 'application/vnd.github+json'
  }
  if ($token) { $apiHeaders['Authorization'] = "Bearer $token" }

  $rel = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/releases/tags/$Tag" -Headers $apiHeaders
  $asset = $rel.assets | Where-Object { $_.name -eq $AssetName } | Select-Object -First 1
  if (-not $asset) {
    throw "Release asset '$AssetName' not found on tag $Tag for $Repository."
  }

  $dlHeaders = @{
    'User-Agent' = 'CmdWarden-Chocolatey'
    'Accept'     = 'application/octet-stream'
  }
  if ($token) { $dlHeaders['Authorization'] = "Bearer $token" }

  # Private GitHub repos need a token. Public repos work without one.
  Get-ChocolateyWebFile -PackageName $packageName -FileFullPath $OutFile -Url $asset.url -ChecksumType 'sha256' -Options @{ Headers = $dlHeaders } -ErrorAction SilentlyContinue
  if (-not (Test-Path $OutFile)) {
    Invoke-WebRequest -Uri $asset.url -Headers $dlHeaders -OutFile $OutFile
  }
}

Write-Host "Downloading $nupkgName from GitHub Release $tag ..."
Get-GitHubReleaseAsset -Repository $repo -Tag $tag -AssetName $nupkgName -OutFile $nupkgPath

if (-not (Test-Path $nupkgPath)) {
  throw "Download failed: $nupkgPath"
}

# Ensure global tools dir is usable
$toolsPath = Join-Path $env:USERPROFILE '.dotnet\tools'
if ($env:Path -notlike "*$toolsPath*") {
  $env:Path = "$toolsPath;$env:Path"
}

# Stop processes that lock a previous tool store
foreach ($n in @('cw', 'cmdwarden', 'CmdWarden.Agent', 'CmdWarden.ApprovalGate')) {
  Get-Process -Name $n -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

$prev = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
cmd /c "dotnet tool uninstall -g CmdWarden >nul 2>&1" | Out-Null
$store = Join-Path $env:USERPROFILE '.dotnet\tools\.store\cmdwarden'
if (Test-Path $store) {
  Remove-Item -Recurse -Force $store -ErrorAction SilentlyContinue
}
$ErrorActionPreference = $prev

$source = Split-Path $nupkgPath -Parent
Write-Host "dotnet tool install -g CmdWarden --add-source $source --version $version"
& dotnet tool install -g CmdWarden --add-source $source --version $version
if ($LASTEXITCODE -ne 0) {
  throw "dotnet tool install failed with exit code $LASTEXITCODE"
}

$cw = Join-Path $toolsPath 'cw.exe'
if (-not (Test-Path $cw)) {
  throw "Install finished but cw.exe was not found at $cw"
}

Write-Host "CmdWarden $version installed. Run: cw version && cw doctor" -ForegroundColor Green
