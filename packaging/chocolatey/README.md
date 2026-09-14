# Chocolatey package: `cmdwarden`

Installs CmdWarden as a **global dotnet tool** by downloading `CmdWarden.<version>.nupkg` from GitHub Releases.

## Prerequisites

- [Chocolatey](https://chocolatey.org/install)
- [.NET 10](https://dotnet.microsoft.com/download) (`dotnet` on PATH)
- For **private** GitHub repos: `gh auth login` **or** `GH_TOKEN` / `GITHUB_TOKEN`

## Install from this repo (local source)

```powershell
cd packaging\chocolatey\cmdwarden

# Build the Chocolatey package (.nupkg for choco, not the CmdWarden tool nupkg)
choco pack

# Install from current directory as the source
choco install cmdwarden -y -s .

# Or with force reinstall
choco upgrade cmdwarden -y -s . --force
```

Uninstall:

```powershell
choco uninstall cmdwarden -y
```

## Publish to chocolatey.org (community)

1. Create an account at https://community.chocolatey.org/
2. Get an API key from account settings
3. Review moderation rules: https://docs.chocolatey.org/en-us/community-repository/moderation/
4. Update `cmdwarden.nuspec` version + release notes for each release
5. Prefer embedding **checksums** in `chocolateyinstall.ps1` for the GitHub asset
6. Pack and push:

```powershell
cd packaging\chocolatey\cmdwarden
# bump <version> in cmdwarden.nuspec first
choco pack
choco push cmdwarden.<version>.nupkg --source https://push.chocolatey.org/ --api-key <YOUR_KEY>
```

**Private repo note:** community packages must download from a URL Chocolatey moderation can access. Publish GitHub Releases as **public**, or host the nupkg on a public URL / your own internal Chocolatey feed.

## Internal / company feed

```powershell
choco pack
# push to your NuGet v2/v3 feed, then:
choco install cmdwarden -y -s https://your-feed/chocolatey
```

## Parameters

```powershell
choco install cmdwarden -y -s . --params "'/Repo:YourOrg/CmdWarden'"
```

## Version sync

When you cut `v0.2.0`:

1. Bump product version in `src/CmdWarden.Cli` (and related projects)
2. Tag + GitHub Release (Assets: `CmdWarden.0.2.0.nupkg`)
3. Set `<version>0.2.0</version>` in `cmdwarden.nuspec`
4. `choco pack` and distribute / push
