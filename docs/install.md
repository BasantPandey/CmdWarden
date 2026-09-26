# Install CmdWarden

CmdWarden installs as a global **dotnet tool** on Windows. Package: **CmdWarden**. Commands: **`cw`** and **`cmdwarden`**. Current version: **0.1.0**.

| Section | Go there when |
|---------|---------------|
| [1. Requirements](#1-requirements) | You start from a clean machine |
| [2. Install](#2-install) | You install for the first time |
| [3. Verify](#3-verify) | You check the install |
| [4. What you get](#4-what-you-get) | You want to see the app before you set it up |
| [5. First-time setup](#5-first-time-setup) | You protect your first tool |
| [6. Other install ways](#6-other-install-ways) | The installer script does not fit |
| [7. Update](#7-update) | A new build is out |
| [8. Uninstall](#8-uninstall) | You remove CmdWarden |
| [9. Troubleshooting](#9-troubleshooting) | Something fails |

---

## 1. Requirements

| Need | Notes |
|------|-------|
| **Windows 10 or 11** | CmdWarden is Windows only |
| **.NET SDK 10** | The installer offers to install it with winget when it is missing. Or [download .NET 10](https://dotnet.microsoft.com/download). |
| **GitHub access** | The repo is private. Sign in to GitHub in the browser to download the setup zip. |

Install is **binaries only**. It does not change how `gh` or `git` run until you run `cw harden`.

---

## 2. Install

1. Download `CmdWarden.<version>-setup.zip` from the [latest Release](https://github.com/BasantPandey/CmdWarden/releases/latest).
2. Extract the zip. It holds the package, `install.cmd`, `uninstall.cmd`, and the two scripts.
3. Double-click **`install.cmd`**. Do not run it as admin.

The installer does these steps and prints each one with `==>`:

1. Checks Windows and the .NET 10 SDK. When the SDK is missing, it asks to install it with winget.
2. Uses the `CmdWarden.<version>.nupkg` next to it. Without one, it downloads the latest GitHub Release.
3. Stops an old Session Agent and removes an old or broken tool install.
4. Runs `dotnet tool install -g CmdWarden`.
5. Adds `%USERPROFILE%\.dotnet\tools` to your user PATH.
6. Creates the Start Menu entry **CmdWarden Vault**. `-Desktop` adds the Desktop icon.
7. Adds **CmdWarden** to Windows Settings > Apps. Its **Uninstall** button runs the uninstaller.
8. Runs `cw version` and `cw doctor`.

From a clone of the repo, run the same installer. It downloads the Release, so run `gh auth login` first:

```powershell
git clone https://github.com/BasantPandey/CmdWarden.git
cd CmdWarden
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Install-CmdWarden.ps1 -Desktop
```

*Expect:* the last lines read `CmdWarden 0.1.0 installed as a global dotnet tool.` and list the next commands.

Options:

```powershell
.\scripts\Install-CmdWarden.ps1 -Version 0.1.0 -Force                 # one version, clean reinstall
.\scripts\Install-CmdWarden.ps1 -PackagePath C:\packages\CmdWarden.0.1.0.nupkg   # local nupkg, no download
.\scripts\Install-CmdWarden.ps1 -Mode zip -InstallDir "$env:LOCALAPPDATA\CmdWarden\app"   # portable, no global tool
.\scripts\Install-CmdWarden.ps1 -SkipDoctor                           # do not run cw doctor
.\scripts\Install-CmdWarden.ps1 -Yes                                  # install the .NET SDK without a question
```

---

## 3. Verify

Open a **new** terminal, then run:

```powershell
cw version
cw doctor
```

*Expect:*

```text
CmdWarden 0.1.0
CLI: cw (alias: cmdwarden)
```

```text
CmdWarden doctor
  product: CmdWarden 0.1.0
  agent binary: ...\agent\CmdWarden.Agent.dll
  vault UI binary: ...\secrets-manager\CmdWarden.SecretsManager.exe
  vault Start Menu shortcut: present
  session agent: ALIVE
  agent version: 0.1.0
```

`cw doctor` starts the Session Agent when it is not running. State lives under `%LOCALAPPDATA%\CmdWarden\`.

The package holds these parts:

| Part | Role |
|------|------|
| `cw.exe`, `cmdwarden.exe` | CLI |
| `agent\` | Session Agent: policy, vault, audit, approval |
| `agent\approval-gate\` | Approval Gate card |
| `secrets-manager\` | CmdWarden Vault desktop app |
| `shim-payload\` | PATH shims that `cw harden` installs |

---

## 4. What you get

### Approval Gate

The card appears on your desktop when policy does not auto-allow a secret release. The command waits until you click **Deny**, **Allow for session**, or **Approve Once**. Or press **Esc**, **A**, or **Enter**.

![Approval Gate card](images/approval-gate.png)

### CmdWarden Vault

Open **CmdWarden Vault** from the Start Menu or the Desktop icon. Six pages. Every page is read-only except Secrets. The app never shows a secret value.

**Secret Gates** - defaults per launcher kind, one card per enrolled launcher, and active session allows.

![Secret Gates page](images/vault-secret-gates.png)

**Detectors** - one card per residual risk, with evidence and the `cw` command that fixes it.

![Detectors page](images/vault-detectors.png)

**Hardened Tools** - one card per catalog tool with its harden state.

![Hardened Tools page](images/vault-hardened-tools.png)

**Secrets** - every secret name in the vault. Add with **Ctrl+N**. Select with the arrow keys and delete with **Del**. [All keys](vault.md#keys).

![Secrets page](images/vault-secrets.png)

**Secret Usage** - the newest gate decisions with launcher, tool, secret name, and decision.

![Secret Usage page](images/vault-secret-usage.png)

**Doctor** - agent health, binary and shortcut checks, and the details list.

![Doctor page](images/vault-doctor.png)

---

## 5. First-time setup

Install alone does not protect a tool. Run this once.

In **your normal terminal**:

```powershell
cw policy enroll --kind terminal
cw harden gh
```

In the **AI harness terminal** (Cursor, Claude Code, Codex panel):

```powershell
cw policy enroll --kind ai-harness
```

Open a new terminal, then check:

```powershell
where.exe gh          # first hit is under %LOCALAPPDATA%\CmdWarden\shims
gh auth status
cw policy list
```

Your terminal is **Trusted**. The harness is **Read**. Continue with the **[Policy quick start](policy-quickstart.md)** for `git`, `az`, `docker`, profiles, and scripts.

---

## 6. Other install ways

### From a Release nupkg by hand

Download `CmdWarden.<version>.nupkg` from the [Releases page](https://github.com/BasantPandey/CmdWarden/releases) into a folder, then:

```powershell
$pkgDir = "C:\packages"
dotnet tool uninstall -g CmdWarden 2>$null
dotnet tool install -g CmdWarden --add-source $pkgDir --version 0.1.0
cw shortcut install --desktop
cw doctor
```

`--add-source` takes the **folder**, not the nupkg file.

### From source

Use this to install what is on `main` right now.

```powershell
git clone https://github.com/BasantPandey/CmdWarden.git
cd CmdWarden
dotnet pack src/CmdWarden.Cli/CmdWarden.Cli.csproj -c Release -o .\artifacts\nupkg
cw agent stop 2>$null
dotnet tool uninstall -g CmdWarden 2>$null
dotnet tool install -g CmdWarden --add-source .\artifacts\nupkg --version 0.1.0
cw shortcut install --desktop
cw doctor
```

The pack builds the CLI, Session Agent, Approval Gate, CmdWarden Vault, shims, and credential helpers.

### Portable zip

Download `CmdWarden.<version>-win-x64.zip` from the Release, extract it, and run `.\cw.exe`. No global tool. Needs the .NET 10 runtime.

### GitHub Actions artifact

Every green **build** on `main` uploads a Windows exe layout. Open [Actions](https://github.com/BasantPandey/CmdWarden/actions), pick the latest run, download the artifact, extract, and run `.\cw.exe`.

### Package managers

Templates for Chocolatey, winget, and Scoop live under [packaging/](https://github.com/BasantPandey/CmdWarden/tree/main/packaging). The public catalogs need a public download URL, so they work today only against a local or internal feed.

---

## 7. Update

Run `cw update`:

```powershell
cw update --check   # only say if a newer release is available
cw update           # install the newest release
```

`cw update` reads the newest GitHub Release and downloads its setup zip. It compares the sha256 of the zip with the digest that GitHub shows for the asset. A wrong or missing digest stops the update. Then the installer of the zip runs in a new window with `-Force -Yes`, and `cw` stops. The installer stops the Session Agent and replaces the tool.

`cw update` installs the dotnet tool. For a portable zip install, download the new zip.

From a clone, the installer script updates in place:

```powershell
cd path\to\CmdWarden
git pull origin main
.\scripts\Install-CmdWarden.ps1 -Force -Desktop
```

From source, the version stays `0.1.0` while code changes, so `dotnet tool update` does nothing. Uninstall, then install again:

```powershell
git pull origin main
dotnet pack src/CmdWarden.Cli/CmdWarden.Cli.csproj -c Release -o .\artifacts\nupkg
cw agent stop
dotnet tool uninstall -g CmdWarden
dotnet tool install -g CmdWarden --add-source .\artifacts\nupkg --version 0.1.0
cw shortcut install --desktop
cw doctor
```

Run `cw shortcut install` after every reinstall. The shortcuts point at the exe inside the tool folder, and a reinstall replaces that folder.

---

## 8. Uninstall

Use one of these:

- Run `cw uninstall`. It runs the same uninstaller as Settings > Apps, in a new window.
- Open Windows **Settings > Apps**, select **CmdWarden**, and click **Uninstall**.
- Double-click **`uninstall.cmd`** from the setup zip or from `scripts\`.
- Run the script:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Uninstall-CmdWarden.ps1
```

The uninstaller works when `cw` is broken or gone. Each step runs on its own. A failed step prints a warning, and the next step continues.

1. Runs `cw unharden` for `gh`, `git`, and `docker`, so their logins go back to the stock stores. With no working `cw`, it removes the CmdWarden credential helper from the git and docker config.
2. Stops the Session Agent, the Approval Gate, and CmdWarden Vault.
3. Removes the Start Menu entry and the Desktop icon.
4. Uninstalls the dotnet tool and clears its tool store.
5. Removes CmdWarden folders from the user PATH. For the machine PATH, it asks for one admin prompt.
6. Deletes `%LOCALAPPDATA%\CmdWarden`: policy, pins, shims, and audit.
7. Asks before it deletes saved secrets from Credential Manager (`CmdWarden/...`). The default is to keep them.
8. Removes the entry in Settings > Apps.

At the end, it lists each item that you must check by hand.

Options:

```powershell
.\scripts\Uninstall-CmdWarden.ps1 -KeepData          # keep policy, pins, and audit, for a reinstall
.\scripts\Uninstall-CmdWarden.ps1 -RemoveSecrets     # also delete saved secrets
.\scripts\Uninstall-CmdWarden.ps1 -Quiet             # ask nothing; keep secrets; skip the admin PATH step
```

---

## 9. Troubleshooting

| Symptom | Fix |
|---------|-----|
| `cw` not found | Open a **new** terminal. Check `%USERPROFILE%\.dotnet\tools` is on the user PATH. |
| Installer says `No latest release found` or the download gets 404 | Use the setup zip: it has the package inside. Or run `gh auth login`, or set `$env:GH_TOKEN` with repo read access. |
| Installer says `The .NET 10 SDK was not found` | Run `winget install --id Microsoft.DotNet.SDK.10 --exact`, open a new terminal, and run the installer again. |
| `cw` is broken and you want it gone | Run the uninstaller (section 8). It does not need a working `cw`. |
| `dotnet tool install` cannot find the package | Pass the **folder** that holds the nupkg to `--add-source`. |
| `dotnet tool uninstall` says `Access to the path ... is denied` | A Session Agent runs from the tool folder. Run `cw agent stop`. If it still fails, stop it from an admin terminal or end the `dotnet` process that runs `CmdWarden.Agent.dll`. |
| `cw doctor` says agent binary missing | Reinstall from a full pack. `agent\` must sit inside the tool package. |
| `cw doctor` says pipe exists but access denied | An agent runs elevated or as another user. Run `cw agent stop` from that terminal, then `cw agent start` from a normal one. |
| `cw shortcut install` says `Vault UI binary not found` | Pack from source so `secrets-manager\` is inside the package, or set `CW_SECRETS_MANAGER_PATH`. |
| Shortcut opens nothing after reinstall | Run `cw shortcut install --desktop` again. |
| No Approval Gate card | Check `agent\approval-gate\CmdWarden.ApprovalGate.exe` exists under the tool install. A missing card fails closed. |
| `Unknown command: shortcut` | The installed `cw` is old. Update (section 7). |
| Wrong or old version | `dotnet tool list -g`, then uninstall and install with `--version`. |

Tool install location:

```text
%USERPROFILE%\.dotnet\tools\cw.exe
%USERPROFILE%\.dotnet\tools\.store\cmdwarden\<version>\cmdwarden\<version>\tools\net10.0\any\
```

---

Next: [Policy quick start](policy-quickstart.md) · [User guide](user-guide.md) · [README](index.md)
