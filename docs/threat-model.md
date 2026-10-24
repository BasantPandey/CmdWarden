---
title: Threat model
description: What CmdWarden stops when an AI agent goes after your secrets, what only slows it, what stays open, and the test for each.
---

# Threat model

This page lists the attacks that CmdWarden expects from an AI agent, and the answer to each one. Each answer links to the test that checks it. Found a gap? Open a [private security advisory](https://github.com/BasantPandey/CmdWarden/security/advisories/new).

## What CmdWarden protects

- The tokens and keys of your CLI tools: `gh`, `git`, `az`, `docker`, `npm`, `aws`, `kubectl`, and ssh keys.
- The vault entries that you save with `cw save`: API keys, passwords, and TOTP keys.
- The policy: which app may use which secret, and when you get the card.

## Who attacks

An AI agent that runs commands on your PC. It can follow a prompt injection from a web page, an issue, or a file. It runs as your Windows user, unless you give it its own account.

CmdWarden does not defend against malware with admin rights, or against a person at your keyboard.

## Status words

| Status | Meaning |
|--------|---------|
| **Stopped** | The attack fails. A test checks it. |
| **Slowed** | The easy forms fail. A careful attacker who runs as your user can still get past. |
| **Open** | CmdWarden does not stop it. The page says what to do. |

## Attacks and answers

| Attack | Answer | Status |
|--------|--------|--------|
| The agent asks a tool for its token: `gh auth token`, `git credential fill`, `az account get-access-token` | The class is `secret-reveal`. You get the card, and Windows Hello when it is set up. A session grant never covers it. Test: [GhCommandClassifierTests](https://github.com/BasantPandey/CmdWarden/blob/main/tests/CmdWarden.Tests/GhCommandClassifierTests.cs), [GitCommandClassifierTests](https://github.com/BasantPandey/CmdWarden/blob/main/tests/CmdWarden.Tests/GitCommandClassifierTests.cs), [AzCommandClassifierTests](https://github.com/BasantPandey/CmdWarden/blob/main/tests/CmdWarden.Tests/AzCommandClassifierTests.cs) | Stopped |
| The agent runs `cw read NAME` to see a vault value | The policy hook stops it. In your own terminal, the card asks first, and Windows Hello when it is set up. Test: [HookBypassTests](https://github.com/BasantPandey/CmdWarden/blob/main/tests/CmdWarden.Tests/HookBypassTests.cs), [V080ProcessTests](https://github.com/BasantPandey/CmdWarden/blob/main/tests/CmdWarden.Tests/V080ProcessTests.cs) | Stopped |
| The agent runs a `cw` command that turns protection off, such as `cw policy set`, `cw unharden`, or `cw agent stop` | The policy hook stops it, also inside `cmd /c`, `powershell -Command`, `-EncodedCommand`, `bash -c`, `Start-Process`, and `dotnet cw.dll`. Test: [HookBypassTests](https://github.com/BasantPandey/CmdWarden/blob/main/tests/CmdWarden.Tests/HookBypassTests.cs) | Slowed: see [what stays open](#what-stays-open) |
| The agent sends keys or clicks to the card, to approve itself | The card takes Approve only from real keyboard or mouse input. Test: [ApprovalGateInjectedInputTests](https://github.com/BasantPandey/CmdWarden/blob/main/tests/CmdWarden.Tests/ApprovalGateInjectedInputTests.cs) | Stopped |
| The agent changes a script after you approve it | The approval holds the file hash. The file stays locked until the run ends. Test: [BoundFilesProcessTests](https://github.com/BasantPandey/CmdWarden/blob/main/tests/CmdWarden.Tests/BoundFilesProcessTests.cs) | Stopped |
| The agent hides code in a PowerShell wrapper, so the card shows a harmless command | A hidden command forces the card, at every level. Test: [PowerShellWrapperProcessTests](https://github.com/BasantPandey/CmdWarden/blob/main/tests/CmdWarden.Tests/PowerShellWrapperProcessTests.cs) | Stopped |
| A prompt injection tells the agent to hunt for tokens | Canary tokens look real. A use of one raises the alarm and blocks the app. Test: [LeakGuardTests](https://github.com/BasantPandey/CmdWarden/blob/main/tests/CmdWarden.Tests/LeakGuardTests.cs) | Stopped when it touches a canary |
| A secret value shows in tool output, and the model reads it | The leak guard puts `[CmdWarden: NAME]` in place of each vault value. Test: [LeakGuardProcessTests](https://github.com/BasantPandey/CmdWarden/blob/main/tests/CmdWarden.Tests/LeakGuardProcessTests.cs) | Stopped for vault values |
| The agent sends `cw://NAME` to a host that the key does not list | The proxy gives `403`, and the value stays in the vault. Test: [KeyProxyProcessTests](https://github.com/BasantPandey/CmdWarden/blob/main/tests/CmdWarden.Tests/KeyProxyProcessTests.cs) | Stopped |
| The agent puts a sign-in key in another field, or in a header, to read it back | A sign-in key goes only in its field, on its path, on its host. Each fill shows the card. Test: [KeyProxyTests](https://github.com/BasantPandey/CmdWarden/blob/main/tests/CmdWarden.Tests/KeyProxyTests.cs), [KeyProxyProcessTests](https://github.com/BasantPandey/CmdWarden/blob/main/tests/CmdWarden.Tests/KeyProxyProcessTests.cs) | Stopped |
| The agent signs a commit or pushes with your ssh key | The ssh gate asks the policy before each sign. Test: [SshGateProcessTests](https://github.com/BasantPandey/CmdWarden/blob/main/tests/CmdWarden.Tests/SshGateProcessTests.cs) | Stopped |
| The agent sends a broken or two-way request to the proxy, to hide a second request in the body | The proxy refuses a request with two `Content-Length` values, both `Content-Length` and `Transfer-Encoding`, or a `Transfer-Encoding` that does not end with `chunked`. A chunk or body over the limit stops before it is read. Test: [FuzzTests](https://github.com/BasantPandey/CmdWarden/blob/main/tests/CmdWarden.Tests/FuzzTests.cs) | Stopped |
| The agent gives a false reason on the card | The card labels it **The agent says:**. Policy never reads it. New lines and layout characters are removed. Test: [AgentReasonTests](https://github.com/BasantPandey/CmdWarden/blob/main/tests/CmdWarden.Tests/AgentReasonTests.cs) | Stopped as a layout attack; a lie stays a lie |
| An agent under its own Windows account tries to save, delete, or list secrets | That account may only ask the gate. Test: [AgentAccountTests](https://github.com/BasantPandey/CmdWarden/blob/main/tests/CmdWarden.Tests/AgentAccountTests.cs) | Stopped |

## What stays open

These attacks work for an agent that runs as **your own Windows user**. CmdWarden makes them harder or easier to see. It does not stop them.

| Attack | Why it works | What to do |
|--------|--------------|------------|
| A renamed copy of `cw.exe`, a script file, or `eval` runs a `cw` command | The hook reads the command line. It cannot know what a new name or a file does. | Run the agent under its own Windows account. |
| The agent edits `%LOCALAPPDATA%\CmdWarden\policy.json` with a shell command | Your user can write the file. `cw setup` stops the file tools of Claude Code, not a shell. | Run the agent under its own account. Check `cw audit` for decisions that you did not expect. |
| The agent reads Credential Manager directly, with a script that calls `CredRead` | Windows gives your entries to any program of your user. | Run the agent under its own account. |
| The agent calls the real tool by its full path, not through the shim | The PATH shim sees only calls by name. | Use strong mode (`cw harden gh --strong`): the stock store of the tool is then empty. `cw scan` names the rest. |
| The agent changes a file in a Git install that your user can write, such as a DLL or the shell `usrin\sh.exe` | The pin covers `git.exe` only. Git for Windows 2.5x ships its shells with no signature, so the gate trusts the shells of the pinned install by path. | Install Git for all users, in `Program Files`. Then only an admin can change its files. |
| The agent nests more than 16 wrappers | The hook stops reading at 16 levels. | Nothing extra. The shim still gates each tool call. |

For a hard line, give the agent its own Windows account. The Codex `elevated` sandbox does this, and `cw setup` enrolls its accounts. See [What an AI agent can do to CmdWarden](trust.md#what-an-ai-agent-can-do-to-cmdwarden).
