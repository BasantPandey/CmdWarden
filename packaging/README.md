# Package managers (winget, Scoop, Chocolatey)

The release makes the winget and Scoop manifests for you. Each release has `CmdWarden.<version>-packages.zip` with manifests that carry the real sha256 of the portable zip.

The portable zip `CmdWarden.<version>-win-x64.zip` carries its own .NET runtime. A package manager installs it with no .NET and no admin prompt.

| Manager | Source | Command for the user |
|---------|--------|----------------------|
| **winget** | `winget/` in the packages zip | `winget install BasantPandey.CmdWarden` |
| **Scoop** | `scoop/cmdwarden.json` in the packages zip | `scoop install cmdwarden` (from a bucket) |
| **Chocolatey** | [chocolatey/](chocolatey/) template | `choco install cmdwarden` |

Make the manifests by hand for any zip:

```powershell
./scripts/New-PackageManifests.ps1 -Version 0.8.0 -Zip artifacts/CmdWarden.0.8.0-win-x64.zip -Out artifacts/packages
```

## winget

The installer manifest uses `ArchiveBinariesDependOnPath: true`. winget then puts the install folder on PATH and makes no symlink. `cw.exe` must run from its own folder, because it finds `runtime\` next to it.

Check a manifest before you submit it:

```powershell
winget validate artifacts\packages\winget
winget install --manifest artifacts\packages\winget
```

The release submits the manifests to [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) when the `WINGET_TOKEN` secret is set. The token is a GitHub classic token with the `public_repo` scope. Without it, submit the manifests by hand: `wingetcreate submit --token <token> artifacts\packages\winget`.

## Scoop

Put `cmdwarden.json` in a bucket repository, then:

```powershell
scoop bucket add cmdwarden https://github.com/<you>/scoop-cmdwarden
scoop install cmdwarden
```

The manifest has `checkver` and `autoupdate`, so a bucket bot can follow new releases.

## Chocolatey (quick local)

```powershell
cd packaging\chocolatey\cmdwarden
choco pack
choco install cmdwarden -y -s .
cw version
```

See [chocolatey/README.md](chocolatey/README.md).

## Release secrets

| Secret or variable | Use |
|--------------------|-----|
| `SIGNPATH_API_TOKEN` (secret), `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG`, `SIGNPATH_SIGNING_POLICY_SLUG` (variables) | Sign every exe and dll through SignPath Foundation (free for open source) |
| `NUGET_API_KEY` | Push the dotnet tool to NuGet.org |
| `WINGET_TOKEN` | Submit the winget manifests |
| `REQUIRE_SIGNING` (variable) | `true` stops a release that is not signed |
