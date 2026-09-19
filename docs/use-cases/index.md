---
title: Use cases
description: Nine short guides for CmdWarden, from gating your GitHub token from an AI agent to undoing everything.
---

# Use cases

Each guide states when to use it, the commands to type, and what to expect. Want good defaults without reading them? See the [Policy quick start](../policy-quickstart.md).

| # | Guide | Goal |
|---|-------|------|
| **UC1** | [Gate your GitHub token from an AI agent](gate-github-token-from-ai-agent.md) | First install, enroll, harden `gh` |
| **UC2** | [Approve or deny a secret read mid-session](approve-secret-read-mid-session.md) | **The main story:** save a secret, the agent tries to read it, you get the card |
| **UC3** | [Let the agent read GitHub, gate writes and token export](agent-reads-github-writes-need-approval.md) | Agent Read auto-allows, write and secret-reveal need a click |
| **UC4** | [Trust your terminal fully, not the agent](trust-terminal-not-agent.md) | Terminal Full, harness Read or Deny |
| **UC5** | [One-shot secret for a script](one-shot-secret-for-script.md) | `save`, `inject`, `delete` |
| **UC6** | [Gate git, az, and docker](gate-git-az-docker.md) | Harden the other tools, strong mode |
| **UC7** | [Audit and scan when something feels wrong](audit-and-scan.md) | `doctor`, `whoami`, `scan`, `audit` |
| **UC8** | [Undo or uninstall](undo-and-uninstall.md) | Unenroll, uninstall |
| **UC9** | [Add and remove secrets in CmdWarden Vault](vault-add-remove-secrets.md) | No CLI, desktop app only |

New here? Start with **UC1**, then read **UC2**. Terms you meet along the way are in the [Glossary](../glossary.md).
