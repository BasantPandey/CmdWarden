# Package managers (Chocolatey, winget, Scoop)

CmdWarden is **not** on the public Chocolatey / winget / Scoop community catalogs by default. This folder holds **templates** you can use:

1. **Locally** (your machine / internal feed)
2. To **submit** to public catalogs when the Release assets are publicly downloadable

| Manager | Path | Best for |
|---------|------|----------|
| **Chocolatey** | [chocolatey/](chocolatey/) | `choco install cmdwarden` (local or community) |
| **winget** | [winget/](winget/) | Windows Package Manager manifests |
| **Scoop** | [scoop/](scoop/) | Portable apps / developer machines |
| **PowerShell** | [../scripts/Install-CmdWarden.ps1](../scripts/Install-CmdWarden.ps1) | Works today against GitHub Releases |

Also always available:

```powershell
# Recommended today
.\scripts\Install-CmdWarden.ps1
# or from Release nupkg
dotnet tool install -g CmdWarden --add-source C:\packages --version 0.1.0
```

## Private repository limitation

Community **chocolatey.org**, **winget-pkgs**, and public **Scoop** buckets require a **public** download URL for automation and moderation. If the GitHub repo (or releases) are private:

| Approach | Works? |
|----------|--------|
| Local `choco pack` + `choco install -s .` | Yes (with `gh` auth or `GH_TOKEN` at install time) |
| Internal Chocolatey/NuGet feed | Yes |
| Public chocolatey.org / winget / scoop | Needs public release assets (or mirror) |
| `Install-CmdWarden.ps1` | Yes with `gh auth login` or token |

---

## Chocolatey (quick local)

```powershell
# Install Chocolatey if needed: https://chocolatey.org/install
cd packaging\chocolatey\cmdwarden
choco pack
choco install cmdwarden -y -s .
cw version
```

See [chocolatey/README.md](chocolatey/README.md).

---

## winget (local manifest)

```powershell
# Install App Installer / winget from Microsoft Store if needed
winget validate .\packaging\winget
winget install --manifest .\packaging\winget
```

Public listing: open a PR to [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) (installer URL must be world-readable). Update `InstallerSha256` when you cut a new release.

---

## Scoop (custom bucket)

```powershell
# Example: add this file to your own bucket repo, then:
scoop bucket add mybucket https://github.com/you/scoop-bucket
scoop install cmdwarden
```

Or install the JSON from a path (bucket-dependent). Update `hash` when the zip changes.

---

## Version bump checklist

When releasing `v0.2.0`:

1. GitHub Release with `CmdWarden.0.2.0.nupkg` + zip
2. Chocolatey: bump `cmdwarden.nuspec` `<version>`
3. winget: bump `PackageVersion` + `InstallerUrl` + `InstallerSha256`
4. Scoop: bump `version`, `url`, `hash`
5. Re-pack / re-submit as needed
