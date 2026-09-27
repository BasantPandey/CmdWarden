---
title: Trust CmdWarden - what it sends, how to check it, what an agent can do
description: CmdWarden sends nothing about you. Check the signatures, read the code, and see what an AI agent can and cannot do to CmdWarden itself.
---

# Trust

A security tool must earn your trust. This page tells you what CmdWarden sends, how to check the files, and what an AI agent can do to CmdWarden itself. Each claim names the code that makes it true.

## What leaves your PC

**Nothing about you.** CmdWarden has no telemetry, no account, and no server of its own.

CmdWarden uses the network only for these commands:

| Command | Talks to | Why |
|---------|----------|-----|
| `cw update` | `api.github.com`, `github.com` | Reads the newest release and downloads its setup zip. It checks the sha256 of the zip first. |
| `cw github app` | `api.github.com` | Gets a GitHub App token for one repo that ends in one hour. Only when you set it up. |
| `cw proxy` | The hosts you list | Puts your API key in place of `cw://NAME`, only for the hosts you name. |

Everything else runs on your PC. The Session Agent listens on a named pipe for your user only, not on a network port.

## Where your data lives

| Data | Place |
|------|-------|
| Secret values | Windows Credential Manager, under `CmdWarden/`. Only the Session Agent reads them, and only after the policy or you allow it. |
| Policy, pins, shims, audit | `%LOCALAPPDATA%\CmdWarden` |
| Audit rows | Names, tools, launchers, and decisions. **Never a secret value.** A release writes its audit row first. When the row cannot be written, nothing is released. |

## Check the files

Every CmdWarden `exe` and `dll` carries the company name CmdWarden. A signed release gives each one a valid signature. Check them:

```powershell
Get-ChildItem "$env:LOCALAPPDATA\CmdWarden\app" -Recurse -Include cw.exe, CmdWarden.*.exe |
  Get-AuthenticodeSignature | Format-Table Status, Path
```

The files in `runtime\` are the .NET runtime of Microsoft, with the signature of Microsoft.

Check a download before you run it: compare `Get-FileHash <zip>` with the sha256 on the [release page](https://github.com/BasantPandey/CmdWarden/releases). `cw update` does this check for you.

CmdWarden is open source under the MIT license. Read the code, or build it yourself: `pwsh ./scripts/Build-Portable.ps1`.

## What an AI agent can do to CmdWarden

An agent runs commands on your PC. So the fair question is: can it turn CmdWarden off?

**What stops it:**

| Protection | What it does |
|------------|--------------|
| The policy hook | A harness that runs a `cw` command that weakens protection gets a deny with "Ask the user". This covers `cw policy set`, `enroll`, `unenroll`, `low-risk`, and `hello`; `cw unharden`; `cw uninstall`; `cw delete`; `cw agent stop`; the `uninstall` of `hook`, `leak-guard`, `mcp`, and `protect`; `cw canary remove`; and the `proxy` changes. |
| Claude Code deny rules | `cw setup` adds `Edit` and `Write` deny rules for `~/AppData/Local/CmdWarden/**`, so the file tools of Claude Code cannot change the policy file. |
| Real input only | The Approval Gate accepts Approve only from real keyboard or mouse input. A program that sends keys or clicks to the card gets nothing. |
| Windows Hello | A secret read asks for Windows Hello after Approve (`cw policy hello`). |
| The launcher check | Policy follows the real process chain and the file hash or signature of the app, not a name the app gives. |
| Agent accounts | When an agent runs under its own Windows account, such as the Codex sandbox accounts, that account is the launcher. It may only ask the gate. It cannot save, delete, or list secrets, change grants, or edit files in your profile. |

**What does not stop it, and what to do:**

- An agent that runs as **your own Windows user** can write files that your user can write. A script can edit `%LOCALAPPDATA%\CmdWarden\policy.json` without the hook seeing a `cw` command. The hook and the deny rules make this hard, not impossible.
- For a hard line, run the agent under its own Windows account. The Codex `elevated` sandbox does this. `cw setup` enrolls its accounts.
- Watch the tray icon and `cw audit`. A policy change that you did not make shows up as decisions that you did not expect.

## Report a problem

Found a way around CmdWarden? Open a [security advisory](https://github.com/BasantPandey/CmdWarden/security/advisories/new) instead of a public issue.
