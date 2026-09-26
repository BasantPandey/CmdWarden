# CLI reference

Every command prints plain text. Colors turn off when you pipe the output or set `NO_COLOR`.

| Command | Role |
|---------|------|
| `cw version` | Product / CLI version |
| `cw update [--check]` | Install the newest release. The setup zip must match the sha256 digest of the release |
| `cw uninstall` | Run the uninstaller of Settings > Apps > CmdWarden |
| `cw doctor` | Session Agent health (lazy-start) |
| `cw agent start\|stop\|status` | Explicit agent control |
| `cw whoami` | Current launcher identity |
| `cw policy enroll --kind terminal\|ai-harness [--key …]` | Enroll launcher |
| `cw policy enroll --account <name> [--kind ai-harness]` | Enroll a Windows agent account, for example `CodexSandboxOffline`, as a launcher. The Session Agent pipe then accepts it |
| `cw policy list` | Enrolled launchers + defaults |
| `cw policy set <key> <tool> <Deny\|Read\|Trusted\|Full>` | Per tool × launcher level; tool `"*"` sets every tool |
| `cw policy unenroll <key>` | Remove enrollment |
| `cw policy sessions [--revoke <id> \| --revoke-all]` | List or revoke active session allows |
| `cw policy hello off\|secret-reveal\|write-and-up` | When the Approval Gate asks for Windows Hello after Approve (default `secret-reveal`) |
| `cw policy low-risk ask\|allow` | `allow`: a low-risk write of an enrolled launcher, like a push to a feature branch, runs with no popup (default `ask`) |
| `cw harden gh\|git\|az\|docker` | Pin + PATH shim (+ gh token import) |
| `cw harden npm\|aws\|kubectl\|<pack tool>` | Pin + PATH shim from a [tool pack](tool-packs.md) |
| `cw harden ssh [--upstream <pipe>]` | Ask before a sign with an ssh key; see [Gate ssh key use](use-cases/gate-ssh-keys.md) |
| `cw github app setup\|status\|remove` | gh gets a [GitHub App token](use-cases/short-lived-github-tokens.md) for one repo that ends in one hour |
| `cw proxy setup [--port N] [--trust]` | Make the per-user CA and turn on the [placeholder proxy](use-cases/api-keys-through-proxy.md) on 127.0.0.1 |
| `cw proxy add <NAME> --host <host>` / `remove <NAME>` | Put the vault entry NAME in place of `cw://NAME` for these hosts |
| `cw proxy list\|strict on\|off\|uninstall` | Show the proxy; `strict on` gives 403 to a host that no key lists; remove the CA and config |
| `cw harden --list` | One row per catalog tool and tool pack, same probe as `cw doctor` |
| `cw harden gh\|git\|docker\|az --strong` | Also move the tool's stock credentials into the Vault |
| `cw unharden gh\|git\|docker\|az\|ssh\|<pack tool>` | Restore the stock store, remove pin and shim |
| `cw doctor --fix-path` | Put the shims dir first on the machine PATH (one UAC prompt) |
| `cw save <NAME>` / `cw delete <NAME>` | Named vault secret |
| `cw inject +NAME -- <cmd>` | Run cmd with secret in child env only |
| `cw scan [--move-to-vault]` | Read-only residual risk scan; `--move-to-vault` moves plain MCP server secrets into the vault |
| `cw launch claude\|codex\|cursor [-- args]` | Start an AI harness without the token variables `cw scan` knows; enroll its binary as ai-harness if needed |
| `cw leak-guard install\|uninstall claude\|cursor` | Hook that replaces vaulted secret values in tool output with `[CmdWarden: NAME]` |
| `cw hook install\|uninstall claude\|cursor` | Hook that checks the policy before the harness runs a shell command. A deny stops the command, and the agent reads "CmdWarden denied this. Ask the user. Do not retry." Allow and ask add no step |
| `cw mcp install\|uninstall claude\|cursor` | Add the CmdWarden MCP server to the harness. Tools: `run_with_secret` (run a program through the gate; the output passes the leak guard), `list_allowed` (policy per tool, secret names only), `why_denied` (the last deny for this launcher) |
| `cw mcp` | Run the MCP server on stdio. The harness starts it |
| `cw canary install [--env <file>]...\|remove\|status` | Fake tokens that block the launcher and show an alarm when used |
| `cw audit [-n N]` | Recent gate decisions |
| `cw shortcut install [--desktop]\|remove\|status` | Start Menu (and Desktop) entry for CmdWarden Vault, a `cw launch` entry per harness on this PC, and the tray icon at logon |

## Screens

**`cw doctor`** - agent health, caller identity, and hardened tools.

![cw doctor](images/cli-doctor.svg)

**`cw whoami`** - the launcher identity for the current shell.

![cw whoami](images/cli-whoami.svg)

**`cw policy list`** - defaults and enrolled launchers.

![cw policy list](images/cli-policy-list.svg)

**`cw harden --list`** - one row per catalog tool.

![cw harden --list](images/cli-harden-list.svg)

**`cw scan`** - residual risk findings.

![cw scan](images/cli-scan.svg)

**`cw audit -n 6`** - the newest gate decisions.

![cw audit](images/cli-audit.svg)

Regenerate these from a real terminal with `uv run scripts/render-cli-shots.py`.
