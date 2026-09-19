# X thread: CmdWarden launch

Post 1 carries the hook and the image. Each later post is one idea. Keep every post under 280 characters.

## Thread

**1/**
Your AI coding agent can run `gh auth token` and read your GitHub token.

On Windows, nothing stops it.

I built CmdWarden. It makes the agent ask you first.

[image: Approval Gate card, docs/images/approval-gate.png]

**2/**
How it works:

- Your terminal stays trusted.
- Claude Code, Cursor, or Codex gets a policy.
- When the agent runs gh, git, az, or docker, a card pops up.
- Deny, Allow for session, or Approve Once.

**3/**
The secret never sits in the agent's shell.

CmdWarden releases it into the one child process for that one run. The parent shell never sees it.

Every decision writes an audit row first, or it fails closed.

**4/**
Setup is four lines:

cw doctor
cw policy enroll --kind terminal
cw policy enroll --kind ai-harness
cw harden gh

Open a new terminal. Done.

[image: docs/images/cli-doctor.svg rendered as PNG]

**5/**
Policy is per tool and per launcher.

Same `gh`, different caller, different rules. Read for the agent, Full for you.

Four levels: Deny, Read, Trusted, Full.

**6/**
There is a desktop app too. CmdWarden Vault shows gates, detectors, hardened tools, secret names, usage, and doctor. It never shows a secret value.

[image: docs/images/vault-secret-gates.png]

**7/**
Open source, C# and .NET 10, Windows 10 and 11, MIT.

Docs with nine short use cases:
https://basantpandey.github.io/CmdWarden/

Code:
https://github.com/BasantPandey/CmdWarden

A star helps other Windows devs find it.

## Single post version

Your AI coding agent can run `gh auth token` and read your GitHub token. On Windows, nothing stops it.

CmdWarden makes it ask you first. Deny, Allow for session, or Approve Once.

Open source, MIT, four lines to set up.
https://basantpandey.github.io/CmdWarden/

[image: Approval Gate card]
