# CmdWarden

CmdWarden gates CLI secret use on Windows by **tool** and **launcher**. Your terminal keeps working. An AI harness (Cursor, Claude Code, Codex) gets a policy and an approval card.

CLI: **`cw`** (alias `cmdwarden`). Desktop app: **CmdWarden Vault**. Stack: C# / .NET 10, Windows only.

![Approval Gate card](docs/images/approval-gate.png)

## What it does

- **Enroll** each launcher (your terminal, each AI harness) with a policy level: Deny, Read, Trusted, or Full.
- **Harden** `gh`, `git`, `az`, and `docker`. A PATH shim asks the Session Agent before the real tool runs.
- **Release** a secret only into the child process for one run. Nothing stays in the parent shell.
- **Ask you** with the Approval Gate card when policy does not auto-allow. No desktop means fail closed.
- **Show** everything in CmdWarden Vault: gates, detectors, hardened tools, secrets, usage, doctor.

## Install

```powershell
git clone https://github.com/BasantPandey/CmdWarden.git
cd CmdWarden
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Install-CmdWarden.ps1 -Desktop
```

The script installs the latest GitHub Release as a global dotnet tool and creates the CmdWarden Vault icons. Private repo: run `gh auth login` first. Details, other install ways, update, and uninstall: **[docs/install.md](docs/install.md)**.

## First five minutes

```powershell
cw doctor
cw policy enroll --kind terminal      # in your normal terminal
cw policy enroll --kind ai-harness    # in the AI harness terminal
cw harden gh
```

Open a new terminal. `gh` now runs through CmdWarden. Your terminal is Trusted. The harness is Read. Everything else prompts you.

Full setup with profiles and per-tool tables: **[docs/policy-quickstart.md](docs/policy-quickstart.md)**.

## Documentation

| Page | Read it when |
|------|--------------|
| [Install](docs/install.md) | You install, update, or remove CmdWarden |
| [Policy quick start](docs/policy-quickstart.md) | You want good policy for `gh`, `git`, `az`, `docker`, and scripts in one page |
| [User guide](docs/user-guide.md) | You want the full story: use cases, Approval Gate, CmdWarden Vault, troubleshooting |
| [AI harness rules](docs/prompts/ai-harness-rules.md) | You paste rules into Cursor, Claude Code, or Codex |
| [Domain glossary](CONTEXT.md) | You need the exact meaning of a term |
| [Architecture](docs/spec/cmdwarden.md) | You work on the code |

## Build from source

Requires the [.NET SDK 10+](https://dotnet.microsoft.com/download) on Windows.

```powershell
dotnet build CmdWarden.sln
dotnet test CmdWarden.sln
```

Pack and install the tool from this checkout:

```powershell
dotnet pack src/CmdWarden.Cli/CmdWarden.Cli.csproj -c Release -o .\artifacts\nupkg
dotnet tool install -g CmdWarden --add-source .\artifacts\nupkg --version 0.1.0
cw shortcut install --desktop
```

| Project | Role |
|---------|------|
| `src/CmdWarden.Contracts` | Domain types, gRPC protos, policy store, command classifiers |
| `src/CmdWarden.Agent` | Per-user Session Agent (gRPC over named pipe) |
| `src/CmdWarden.Cli` | `cw` / `cmdwarden` CLI; packs as the **CmdWarden** dotnet tool |
| `src/CmdWarden.ApprovalGate` | Approval Gate card (Deny / Allow for session / Approve Once) |
| `src/CmdWarden.SecretsManager` | CmdWarden Vault desktop app |
| `src/CmdWarden.Shim.*` | PATH shims for `gh`, `git`, `az`, `docker` |
| `src/CmdWarden.Helper.*` | Credential helpers for strong mode (`git`, `docker`) |
| `tests/CmdWarden.Tests` | Unit and process tests |
| `packaging/` | Chocolatey, winget, and Scoop templates |

Durable state lives under `%LOCALAPPDATA%\CmdWarden\`.

## Status

Version **0.1.0**. All four catalog tools work end to end in compat mode. Strong mode is opt-in for `gh`, `git`, and `docker`. Every secret release writes an audit row first, or it fails closed.

Issues and plans: [GitHub issues](https://github.com/BasantPandey/CmdWarden/issues).
