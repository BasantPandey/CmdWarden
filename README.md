# CmdWarden

[![build](https://github.com/BasantPandey/CmdWarden/actions/workflows/build.yml/badge.svg)](https://github.com/BasantPandey/CmdWarden/actions/workflows/build.yml)
[![docs](https://github.com/BasantPandey/CmdWarden/actions/workflows/docs.yml/badge.svg)](https://basantpandey.github.io/CmdWarden/)
[![release](https://img.shields.io/github/v/release/BasantPandey/CmdWarden?display_name=tag)](https://github.com/BasantPandey/CmdWarden/releases/latest)
[![platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078d4)](docs/install.md)

**Docs site: [basantpandey.github.io/CmdWarden](https://basantpandey.github.io/CmdWarden/)**

CmdWarden makes AI coding agents ask before they use your secrets on Windows. Claude Code, Cursor, and Codex get a policy and an Approval Gate card. Your own terminal keeps working as before.

CLI: **`cw`** (alias `cmdwarden`). Desktop app: **CmdWarden Vault**. Free and open source.

[![CmdWarden demo: the agent asks, you press Deny or Approve Once](docs/images/cmdwarden-promo.gif)](docs/images/cmdwarden-promo.mp4)

Get the [video in high quality (MP4)](docs/images/cmdwarden-promo.mp4).

## Two steps

1. Download `CmdWarden.<version>-setup.zip` from the [latest release](https://github.com/BasantPandey/CmdWarden/releases/latest). Extract it, and double-click **`install.cmd`**.
2. Open a new terminal and run:

```powershell
cw setup
```

No .NET install and no admin prompt: CmdWarden carries its own runtime. `cw setup` enrolls your terminal, hardens the tools it finds, wires Claude Code, Cursor, and Codex, and shows you a real card. Each step asks first.

The winget package and the NuGet.org package come soon ([#64](https://github.com/BasantPandey/CmdWarden/issues/64)). Until then, `winget install` does not find CmdWarden.

See the card with no risk at any time: `cw try`. **Uninstall:** `cw uninstall`.

Details, other install ways, and update: **[docs/install.md](docs/install.md)**. What CmdWarden sends (nothing about you) and what an agent can do: **[docs/trust.md](docs/trust.md)**.

## What it does

- **Gates each tool.** A PATH shim asks the Session Agent before `gh`, `git`, `az`, `docker`, `npm`, `aws`, `kubectl`, or `ssh` uses your login.
- **Knows who asks.** Policy follows the real app behind the command: your terminal, Claude Code, Cursor, Codex, or an unknown install script.
- **Asks only when it matters.** Reads and low-risk writes of an enrolled harness run with no card. Secret reads, unknown commands, and risky writes ask. One **Allow for session** covers the task.
- **Guards `.env` files.** `cw env import` moves the values into the vault. `cw inject --env-file .env -- npm run dev` gives them only to your program, and the output shows `[CmdWarden: NAME]`.
- **Works with your harness.** A policy hook, a leak guard, and an MCP server for Claude Code, Cursor, and Codex.
- **Fails closed.** No desktop means no card, and no card means no secret. Every release writes an audit row first.

Full setup with profiles and per-tool tables: **[docs/policy-quickstart.md](docs/policy-quickstart.md)**.

## Documentation

The full site is at **[basantpandey.github.io/CmdWarden](https://basantpandey.github.io/CmdWarden/)**. The same pages live in [docs/](docs/).

| Page | Read it when |
|------|--------------|
| [Install](docs/install.md) | You install, update, or remove CmdWarden |
| [Trust](docs/trust.md) | You want to know what CmdWarden sends and what an agent can do to it |
| [Policy quick start](docs/policy-quickstart.md) | You want good policy for `gh`, `git`, `az`, `docker`, and scripts in one page |
| [Use cases](docs/use-cases/index.md) | You want a step-by-step guide for one goal, UC1 to UC16 |
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

Build the portable folder, with its own .NET runtime, and set it up:

```powershell
pwsh ./scripts/Build-Portable.ps1 -Out artifacts/portable
pwsh ./scripts/Test-Portable.ps1 -Folder artifacts/portable   # runs it with no .NET on PATH
.\artifacts\portable\cw.exe setup
```

| Project | Role |
|---------|------|
| `src/CmdWarden.Contracts` | Domain types, gRPC protos, policy store, command classifiers |
| `src/CmdWarden.Agent` | Per-user Session Agent (gRPC over named pipe) |
| `src/CmdWarden.Cli` | `cw` / `cmdwarden` CLI; packs as the **CmdWarden** dotnet tool |
| `src/CmdWarden.ApprovalGate` | Approval Gate card (Deny / Allow for session / Approve Once) |
| `src/CmdWarden.SecretsManager` | CmdWarden Vault desktop app |
| `src/CmdWarden.Shim.*` | PATH shims for `gh`, `git`, `az`, `docker`, and the tool packs |
| `src/CmdWarden.TryAgent` | The stand-in agent of `cw try` |
| `src/CmdWarden.Helper.*` | Credential helpers for strong mode (`git`, `docker`) |
| `tests/CmdWarden.Tests` | Unit and process tests |
| `packaging/` | Notes on the winget and Scoop manifests that the release makes |

Durable state lives under `%LOCALAPPDATA%\CmdWarden\`.

Docs site: `uv run --with mkdocs-material mkdocs serve`. CLI screenshots: `uv run scripts/render-cli-shots.py`.

## Status

See the [latest release](https://github.com/BasantPandey/CmdWarden/releases/latest) for the current version. The catalog tools and the tool packs work end to end in compat mode. Strong mode is opt-in for `gh`, `git`, `az`, and `docker`. Every secret release writes an audit row first, or it fails closed.

Issues and plans: [GitHub issues](https://github.com/BasantPandey/CmdWarden/issues). Found a bug or want a tool added? Open an issue. Star the repo if CmdWarden saves you a token.
