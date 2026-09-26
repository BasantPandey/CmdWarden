---
title: Use cases
description: Fourteen short guides for CmdWarden, from gating your GitHub token from an AI agent to undoing everything.
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
| **UC10** | [Gate npm and your npm token](gate-npm.md) | Publish and token commands ask; install scripts never see the token |
| **UC11** | [Gate the AWS CLI](gate-aws.md) | Key-printing and delete commands ask; keys in the vault |
| **UC12** | [Gate kubectl](gate-kubectl.md) | Secret objects, raw kubeconfig, exec, and deletes ask |
| **UC13** | [Gate ssh key use](gate-ssh-keys.md) | A push over ssh from the agent asks; your terminal pushes freely |
| **UC14** | [Short-lived GitHub tokens for one repo](short-lived-github-tokens.md) | gh gets a GitHub App token for the current repo that ends in one hour |
| **UC15** | [Keep API keys out of the agent](api-keys-through-proxy.md) | The agent sends `cw://NAME`; the proxy puts the real key in place for listed hosts only |

New here? Start with **UC1**, then read **UC2**. Terms you meet along the way are in the [Glossary](../glossary.md).
