#Requires -Version 7
<#
.SYNOPSIS
  Stage, put back, and check the CmdWarden files that the release signs (#42).

.DESCRIPTION
  The release signs every exe and dll with the company name CmdWarden, and the two install scripts.
  Third-party files (gRPC, Protobuf) keep the signature of their owner.

  Stage   Copy the files to sign into <Out>\files and write <Out>\manifest.json.
          The files come from the publish folder, from inside the nupkg, and from the script list.
  Apply   Put each signed file back: over the file on disk, or into its entry in the nupkg.
  Verify  Fail when a staged file, or a file on disk from the manifest, has no valid signature.

  A signing tool signs <Out>\files between Stage and Apply: the Trusted Signing action, or signtool.

.EXAMPLE
  ./scripts/Release-Signing.ps1 -Stage -Bin artifacts/bin -Nupkg artifacts/nupkg/CmdWarden.0.7.0.nupkg -Script scripts/Install-CmdWarden.ps1,scripts/Uninstall-CmdWarden.ps1 -Out artifacts/to-sign
  ./scripts/Release-Signing.ps1 -Apply -Out artifacts/to-sign
  ./scripts/Release-Signing.ps1 -Verify -Out artifacts/to-sign
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, ParameterSetName = "Stage")] [switch] $Stage,
    [Parameter(Mandatory, ParameterSetName = "Apply")] [switch] $Apply,
    [Parameter(Mandatory, ParameterSetName = "Verify")] [switch] $Verify,
    [Parameter(ParameterSetName = "Stage")] [string] $Bin,
    [Parameter(ParameterSetName = "Stage")] [string] $Nupkg,
    [Parameter(ParameterSetName = "Stage")] [string[]] $Script = @(),
    [Parameter(Mandatory)] [string] $Out
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$Company = "CmdWarden"
$manifestPath = Join-Path $Out "manifest.json"

function Test-Ours([string] $path) {
    [System.Diagnostics.FileVersionInfo]::GetVersionInfo($path).CompanyName -eq $Company
}

function Add-Staged([System.Collections.Generic.List[object]] $items, [string] $source, [hashtable] $entry) {
    $dir = Join-Path $Out ("files\" + $items.Count)
    New-Item -ItemType Directory -Force $dir | Out-Null
    $staged = Join-Path $dir (Split-Path -Leaf $source)
    Copy-Item -LiteralPath $source -Destination $staged
    $entry.Staged = [System.IO.Path]::GetRelativePath($Out, $staged)
    $items.Add([pscustomobject]$entry)
}

if ($Stage) {
    if (Test-Path $Out) { Remove-Item -Recurse -Force $Out }
    New-Item -ItemType Directory -Force $Out | Out-Null
    $items = [System.Collections.Generic.List[object]]::new()

    if ($Bin) {
        foreach ($f in Get-ChildItem $Bin -Recurse -File -Include *.exe, *.dll) {
            if (Test-Ours $f.FullName) {
                Add-Staged $items $f.FullName @{ Kind = "file"; Target = $f.FullName }
            }
        }
    }

    if ($Nupkg) {
        $nupkgPath = (Resolve-Path $Nupkg).Path
        $temp = Join-Path $Out "nupkg-read"
        $zip = [System.IO.Compression.ZipFile]::OpenRead($nupkgPath)
        try {
            foreach ($e in $zip.Entries | Where-Object { $_.FullName -match '\.(exe|dll)$' }) {
                $file = Join-Path $temp ($e.FullName -replace '/', '\')
                New-Item -ItemType Directory -Force (Split-Path -Parent $file) | Out-Null
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($e, $file, $true)
                if (Test-Ours $file) {
                    Add-Staged $items $file @{ Kind = "nupkg"; Target = $e.FullName; Nupkg = $nupkgPath }
                }
            }
        }
        finally {
            $zip.Dispose()
        }
        Remove-Item -Recurse -Force $temp
    }

    foreach ($s in $Script) {
        $full = (Resolve-Path $s).Path
        Add-Staged $items $full @{ Kind = "file"; Target = $full }
    }

    $items | ConvertTo-Json -Depth 3 -AsArray | Set-Content -Encoding utf8 $manifestPath
    Write-Host "Staged $($items.Count) files to sign in $(Join-Path $Out 'files')"
    return
}

$items = @(Get-Content -Raw $manifestPath | ConvertFrom-Json)

if ($Apply) {
    foreach ($group in $items | Group-Object Kind, Nupkg) {
        $first = $group.Group[0]
        if ($first.Kind -eq "file") {
            foreach ($i in $group.Group) { Copy-Item -Force -LiteralPath (Join-Path $Out $i.Staged) -Destination $i.Target }
            continue
        }
        # Replace each entry in place, so the rest of the package stays byte for byte the same.
        $zip = [System.IO.Compression.ZipFile]::Open($first.Nupkg, [System.IO.Compression.ZipArchiveMode]::Update)
        try {
            foreach ($i in $group.Group) {
                $zip.GetEntry($i.Target).Delete()
                [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $Out $i.Staged), $i.Target, [System.IO.Compression.CompressionLevel]::Optimal)
            }
        }
        finally {
            $zip.Dispose()
        }
    }
    Write-Host "Put back $($items.Count) signed files"
    return
}

if ($Verify) {
    $bad = [System.Collections.Generic.List[string]]::new()
    foreach ($i in $items) {
        $paths = @(Join-Path $Out $i.Staged)
        if ($i.Kind -eq "file") { $paths += $i.Target }
        foreach ($p in $paths) {
            $sig = Get-AuthenticodeSignature -LiteralPath $p
            if ($sig.Status -ne "Valid") { $bad.Add("$p : $($sig.Status) $($sig.StatusMessage)") }
        }
    }
    if ($bad.Count -gt 0) {
        $bad | ForEach-Object { Write-Host $_ }
        throw "$($bad.Count) files have no valid signature."
    }
    Write-Host "All $($items.Count) signed files are valid."
}
