# CmdWarden

[![build](https://github.com/BasantPandey/CmdWarden/actions/workflows/build.yml/badge.svg)](https://github.com/BasantPandey/CmdWarden/actions/workflows/build.yml)
[![docs](https://github.com/BasantPandey/CmdWarden/actions/workflows/docs.yml/badge.svg)](https://basantpandey.github.io/CmdWarden/)
[![release](https://img.shields.io/github/v/release/BasantPandey/CmdWarden?display_name=tag)](https://github.com/BasantPandey/CmdWarden/releases/latest)
[![platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078d4)](docs/install.md)
[![.NET](https://img.shields.io/badge/.NET-10-512bd4)](https://dotnet.microsoft.com/download)

**Docs site: [basantpandey.github.io/CmdWarden](https://basantpandey.github.io/CmdWarden/)**

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

1. Download `CmdWarden.<version>-setup.zip` from the [latest Release](https://github.com/BasantPandey/CmdWarden/releases/latest) and extract it.
2. Double-click **`install.cmd`**.

The installer adds the `cw` command, the CmdWarden Vault icons, and an entry in Windows Settings > Apps. It offers to install the .NET 10 SDK when it is missing.

**Uninstall:** Windows Settings > Apps > **CmdWarden** > **Uninstall**. Or double-click **`uninstall.cmd`**. It works when `cw` is broken.

Details, other install ways, and update: **[docs/install.md](docs/install.md)**.

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

The full site is at **[basantpandey.github.io/CmdWarden](https://basantpandey.github.io/CmdWarden/)**. The same pages live in [docs/](docs/).

| Page | Read it when |
|------|--------------|
| [Install](docs/install.md) | You install, update, or remove CmdWarden |
| [Policy quick start](docs/policy-quickstart.md) | You want good policy for `gh`, `git`, `az`, `docker`, and scripts in one page |
| [Use cases](docs/use-cases/index.md) | You want a step-by-step guide for one goal, UC1 to UC9 |
| [How it works](docs/concepts.md) | You want the model: launchers, policy, shims, the Vault, the Approval Gate |
| [CmdWarden Vault app](docs/vault.md) | You want every page of the desktop app explained |
| [Troubleshooting](docs/troubleshooting.md) | Something is blocked, missing, or down |
| [CLI reference](docs/cli-reference.md) | You want every `cw` command on one page, with screens |
| [AI harness rules](docs/prompts/ai-harness-rules.md) | You paste rules into Cursor, Claude Code, or Codex |
| [Glossary](CONTEXT.md) | You need the exact meaning of a term |
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
dotnet tool install -g CmdWarden --add-source .\artifacts\nupkg
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

Docs site: `uv run --with mkdocs-material mkdocs serve`. CLI screenshots: `uv run scripts/render-cli-shots.py`.

## Status

Version **0.2.0**. All four catalog tools work end to end in compat mode. Strong mode is opt-in for `gh`, `git`, and `docker`. Every secret release writes an audit row first, or it fails closed.

Issues and plans: [GitHub issues](https://github.com/BasantPandey/CmdWarden/issues). Found a bug or want a tool added? Open an issue. Star the repo if CmdWarden saves you a token.
