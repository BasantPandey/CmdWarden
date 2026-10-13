---
title: Code signing policy - who signs CmdWarden and how
description: Free code signing provided by SignPath.io, certificate by SignPath Foundation. The team roles, the signing rules, and the privacy statement.
---

# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

**Status:** the application to SignPath Foundation is open ([#64](https://github.com/BasantPandey/CmdWarden/issues/64)). Releases up to v0.8.1 are not signed.

## What we sign

The release workflow signs only the files that the CmdWarden team builds from this repository. [Release signing](spec/release-signing.md) lists each file. Third-party files keep the signature of their owner.

Each signed file has the product name `CmdWarden` and the version of the release.

## Team roles

| Role | Members |
|------|---------|
| Committers and reviewers | [BasantPandey](https://github.com/BasantPandey) |
| Approvers | [BasantPandey](https://github.com/BasantPandey) |

- A committer changes the source code with no other review.
- A reviewer checks each pull request from a person who is not a committer.
- An approver approves each signing request by hand.

Every team member uses multi-factor authentication for GitHub and for SignPath.

## How a release gets signed

1. A team member pushes a version tag.
2. The [release workflow](https://github.com/BasantPandey/CmdWarden/blob/main/.github/workflows/release.yml) builds every file from the source on GitHub Actions.
3. The workflow sends the files to SignPath.
4. An approver approves the signing request.
5. The workflow checks each signature, then publishes the release.

## Privacy

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

[Trust](trust.md#what-leaves-your-pc) lists each command that uses the network, and the host it talks to.

## Report a problem

Open an issue at [github.com/BasantPandey/CmdWarden/issues](https://github.com/BasantPandey/CmdWarden/issues). For a security problem, follow [Report a problem](trust.md#report-a-problem).
