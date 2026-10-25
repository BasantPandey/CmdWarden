# CmdWarden

CmdWarden makes AI coding agents ask before they use your secrets on Windows. Claude Code, Cursor, Codex, and Grok get a policy and an Approval Gate card. Your own terminal keeps working as before.

This package is the `cw` CLI as a .NET tool. It is for Windows 10 and 11 only.

## Install

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet tool install -g CmdWarden
cw setup
```

`cw setup` enrolls your terminal, hardens the tools it finds, and wires Claude Code, Cursor, Codex, and Grok. Each step asks first.

No .NET SDK? Use the setup zip from the [latest release](https://github.com/BasantPandey/CmdWarden/releases/latest). It carries its own .NET runtime.

## What it does

- **Gates each tool.** A PATH shim asks the Session Agent before `gh`, `git`, `az`, `docker`, `npm`, `aws`, `kubectl`, or `ssh` uses your login.
- **Knows who asks.** Policy follows the real app behind the command: your terminal, Claude Code, Cursor, Codex, Grok, or an unknown script.
- **Guards `.env` files.** `cw inject --env-file .env -- npm run dev` gives the values only to your program.
- **Signs in for the agent.** The agent types `cw://NAME` in a sign-in form. You approve, and only the site gets the password or the 2FA code. `cw save NAME --generate` makes a new password that nobody sees.
- **Fails closed.** No card means no secret. Every release of a secret writes an audit row first.

See the card with no risk: `cw try`. Remove CmdWarden: `cw uninstall`.

## Links

- [Docs](https://basantpandey.github.io/CmdWarden/)
- [Install and update](https://basantpandey.github.io/CmdWarden/install/)
- [What CmdWarden sends (nothing about you)](https://basantpandey.github.io/CmdWarden/trust/)
- [Source and issues](https://github.com/BasantPandey/CmdWarden)

CmdWarden is free and open source under the MIT license.
