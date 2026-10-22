# Linux (Omarchy) port plan

This plan collects the Linux decisions from issues #10 to #22 (issue #49).

## Status

- The port is a separate product in its own repo: [CmdWarden-Omarchy](https://github.com/BasantPandey/CmdWarden-Omarchy). It is an Inspired Twin, not a shared codebase.
- The first vertical is built. Tickets [#1](https://github.com/BasantPandey/CmdWarden-Omarchy/issues/1) to [#11](https://github.com/BasantPandey/CmdWarden-Omarchy/issues/11) are closed.
- The next slice is to harden the `gh` gate ([#17](https://github.com/BasantPandey/CmdWarden-Omarchy/issues/17)). Its spec is [#21](https://github.com/BasantPandey/CmdWarden-Omarchy/issues/21).

## Platform decisions

| Topic | Decision | Source |
|---|---|---|
| Target | Omarchy (Arch Linux and Hyprland) only. Not generic Linux. | #10 |
| Language | Go. One static binary. | #10 |
| Name | CmdWarden, with the same `cw` command. | #10 |
| Vault | Secret Service through libsecret (gnome-keyring). `gh` already uses it. | #10, #11 |
| Session Agent | A D-Bus session service. systemd `--user` starts it on the first call. | #10 |
| Launcher identity | Provenance Channel: the package manager that owns the binary (`pacman` or `mise`). The Identity Key is `<channel>:<tool>`, for example `mise:claude`. | #13 |
| Updates | An update in the same channel keeps the same launcher. The new hash goes to the audit trail. | #13 |
| Unmanaged Launcher | A binary with no channel gets a path and hash pin. Each change needs a new approval. | #13 |
| Shim | `mise` tools get an Occupied Shim: the shim takes the place of the real file. `pacman` tools get a Path Shim. | #14 |
| Pin Drift | `cw doctor` checks again which channel owns the tool, and shows a change. | #14 |
| Approval Gate | One centered card with a dark backdrop, in one Wayland surface (a Quickshell window). | #12, #15 |
| `gh` token | Harden copies the active `gh` token into the CmdWarden vault, as on Windows. | #11 |

## First vertical: gated `gh`

The first vertical is the same as the Windows Spike Vertical: Shim, Session Agent, identity, Vault, Approval Gate, real `gh`. Compat Mode only.

1. Repo and CLI skeleton ([#1](https://github.com/BasantPandey/CmdWarden-Omarchy/issues/1)).
2. Session Agent start and health ([#2](https://github.com/BasantPandey/CmdWarden-Omarchy/issues/2)).
3. Launcher identity by Provenance Channel ([#3](https://github.com/BasantPandey/CmdWarden-Omarchy/issues/3)).
4. Policy levels, command classes, and enrollment ([#4](https://github.com/BasantPandey/CmdWarden-Omarchy/issues/4)).
5. Secret Service vault and `gh` token import ([#5](https://github.com/BasantPandey/CmdWarden-Omarchy/issues/5)).
6. Approval Gate card, connected to the Session Agent ([#6](https://github.com/BasantPandey/CmdWarden-Omarchy/issues/6)).
7. Occupied Shim and Path Shim ([#7](https://github.com/BasantPandey/CmdWarden-Omarchy/issues/7)).
8. `cw harden gh` ([#8](https://github.com/BasantPandey/CmdWarden-Omarchy/issues/8)).
9. `gh` command classifier ([#9](https://github.com/BasantPandey/CmdWarden-Omarchy/issues/9)).
10. Audit trail ([#10](https://github.com/BasantPandey/CmdWarden-Omarchy/issues/10)).
11. End-to-end test: gated `gh` through the real card ([#11](https://github.com/BasantPandey/CmdWarden-Omarchy/issues/11)).

## What carries over from Windows

The Go code shares no code with this repo. It keeps these rules the same:

- The terms in [CONTEXT.md](https://github.com/BasantPandey/CmdWarden/blob/main/CONTEXT.md).
- The policy levels (Deny, Read, Trusted, Full) and the command classes (read, write, secret-reveal, unknown). The defaults are the same: ai-harness is Read, terminal is Trusted, unenrolled is Deny.
- The `gh` classification table of the Windows spec.
- The audit row: one NDJSON line per gate decision, with the same fields and no secret values. A failed audit write blocks the release.
- The card buttons: Deny, Approve Once, Allow for session.
- The stop text of the shim run (#22).

## What is new on Linux

- Identity without Authenticode: Provenance Channel and Unmanaged Launcher.
- Two shim shapes, because the `mise` activate hook changes `PATH` at each prompt.
- The card is a Wayland layer-shell window. Omarchy has no notification server that can show three buttons.
- The vault is Secret Service, not Windows Credential Manager.
- The agent starts through D-Bus and systemd, not `cw agent start`.

## Next steps

1. Harden the `gh` gate ([#21](https://github.com/BasantPandey/CmdWarden-Omarchy/issues/21)):
   - Stop harden when Secret Service cannot hold the token ([#22](https://github.com/BasantPandey/CmdWarden-Omarchy/issues/22)).
   - Use an Occupied Shim for a `pacman` `gh` too ([#23](https://github.com/BasantPandey/CmdWarden-Omarchy/issues/23)).
   - Make `gh auth status`, `gh run view --log`, and `gh codespace ports forward` secret-reveal ([#24](https://github.com/BasantPandey/CmdWarden-Omarchy/issues/24)).
2. Then gate `git push` ([#25](https://github.com/BasantPandey/CmdWarden-Omarchy/issues/25)).
3. Packaging (AUR or `omarchy-pkg-install`) waits.

## Out of scope

- `az` and `docker` on Linux.
- Strong Mode and credential helpers.
- A Linux version of CmdWarden Vault.
- Other Linux distributions.
