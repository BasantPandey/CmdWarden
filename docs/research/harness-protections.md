# Research: what AI harnesses do to protect secrets

Ticket: [#57](https://github.com/BasantPandey/CmdWarden/issues/57). Map: [#55](https://github.com/BasantPandey/CmdWarden/issues/55). Research date: 2026-09-27.

## Question

What do Claude Code, Cursor, and Codex do by themselves to protect secrets and gate commands on Windows?
Where does CmdWarden still add value?
Where does a harness feature already do the same job?

## Short answer

All three harnesses now have hooks, command rules, and a classifier or approval flow.
Only Codex has a native Windows sandbox.
Claude Code and Cursor sandbox commands only on macOS, Linux, or WSL2.
Claude Code has the strongest secret features: env scrub and masked credentials behind a proxy.
But the masked credentials need the sandbox, so they do not work on native Windows.
No harness gates a secret by tool and launcher, and no harness asks outside its own window.
No harness knows about Windows Credential Manager or the stored tokens of `gh`, `az`, or `docker`.
Classifier modes are now the default in Claude Code and Cursor, and both vendors say a classifier is not a security boundary.
So CmdWarden stays useful on native Windows as the one fixed, out-of-band gate.
CmdWarden must add Codex to its hooks, leak guard, and MCP server, because Codex now has the hooks for it.

## Versions checked

| Product | Version | Date | How found |
|---|---|---|---|
| Claude Code | 2.1.283 | 2026-09-25 | npm `@anthropic-ai/claude-code` latest, [CHANGELOG](https://github.com/anthropics/claude-code/blob/main/CHANGELOG.md) |
| Cursor (desktop, Windows x64) | 3.22.7 | Checked 2026-09-27 | Cursor download API |
| Codex | rust-v0.157.1 | 2026-09-26 | [openai/codex releases](https://github.com/openai/codex/releases) |
| Windows agent features | Preview | 2025-11 to 2026-06 | Microsoft Learn, Microsoft Support, Windows Developer Blog |

## Claude Code

| Area | What it does | Native Windows? | Source |
|---|---|---|---|
| Sandbox | OS sandbox for Bash, PowerShell, and Monitor commands. Limits files and network. | No. "Native Windows is not supported." Use WSL2. | [Sandboxing](https://code.claude.com/docs/en/sandboxing) |
| Default reads in sandbox | Reads the whole computer. "This default still allows reading credential files such as `~/.aws/credentials` and `~/.ssh/`." | n/a | [Sandboxing](https://code.claude.com/docs/en/sandboxing) |
| `sandbox.credentials` deny | Blocks listed files and unsets listed env vars for sandboxed commands. "There is no built-in credential deny list." | No (sandbox only) | [Sandboxing](https://code.claude.com/docs/en/sandboxing) |
| `sandbox.credentials` mask | Command sees a placeholder. The sandbox proxy puts the real value in requests to `injectHosts`. Needs v2.1.199+. Handles JWT and AWS SigV4 re-sign. | No (sandbox only) | [Sandboxing](https://code.claude.com/docs/en/sandboxing) |
| `CLAUDE_CODE_SUBPROCESS_ENV_SCRUB=1` | Strips Anthropic, cloud, and known credential env vars from Bash, hooks, and MCP stdio children. Off by default. | Yes (PID namespace part is Linux only) | [Env vars](https://code.claude.com/docs/en/env-vars) |
| Permission rules | `allow` / `ask` / `deny` for Bash, PowerShell, Read, WebFetch, MCP. PowerShell rules parse the AST. `Read(//**/.env)` works on Windows paths. | Yes | [Permissions](https://code.claude.com/docs/en/permissions) |
| Rule limits | Bash rules match the command "as written". The docs scope `Read` rules to the file tools. Only the sandbox stops a shell command that reads the file. | Yes | [Permissions](https://code.claude.com/docs/en/permissions) |
| Auto mode | A classifier reviews actions. Default start mode from v2.1.283 (v2.1.233+ on native Windows). Blocks "printing a live credential or token into the transcript". Allows "reading `.env` and sending credentials to their matching API". | Yes | [Permission modes](https://code.claude.com/docs/en/permission-modes) |
| Auto mode limit | "Boundaries are not stored as rules." Context compaction can lose them. "For a hard guarantee, add a deny rule." | Yes | [Permission modes](https://code.claude.com/docs/en/permission-modes) |
| Hooks | `PreToolUse` can allow, ask, deny, or rewrite input. `PostToolUse` `updatedToolOutput` replaces what the model sees for all tools. Hooks can run in PowerShell. | Yes | [Hooks](https://code.claude.com/docs/en/hooks) |
| Own credential store | "Protected by file permissions on Windows and Linux." | Yes | [Security](https://code.claude.com/docs/en/security) |
| Windows shell | PowerShell tool, or Git Bash when Git for Windows is present. Native Windows since the "Added support for native Windows" release. | Yes | [Setup](https://code.claude.com/docs/en/setup), [Tools](https://code.claude.com/docs/en/tools-reference) |

## Cursor

| Area | What it does | Native Windows? | Source |
|---|---|---|---|
| Run Modes | Auto-review (default since 3.6, 2026-05-29), Allowlist, Run Everything. "Ask Every Time" is gone since 3.5. | Yes | [Run Modes](https://cursor.com/docs/agent/security/run-modes) |
| Auto-review classifier | Reviews shell, MCP, and Fetch calls. "Auto-review is not a security boundary." `permissions.json` takes plain English allow and block instructions. | Yes | [Run Modes](https://cursor.com/docs/agent/security/run-modes) |
| Sandbox | macOS Seatbelt, Linux Landlock and seccomp. Docs list no native Windows sandbox. The Cursor blog (2026-02-18) says Windows runs "our Linux sandbox inside WSL2". | No (WSL2 only) | [Run Modes](https://cursor.com/docs/agent/security/run-modes), [Blog](https://cursor.com/blog/agent-sandboxing) |
| Sandbox env | Docs name no env var scrub or secret mask for sandboxed commands. | n/a | [Run Modes](https://cursor.com/docs/agent/security/run-modes) |
| `.cursorignore` | Hides files from Agent reads and Tab. "The terminal and MCP server tools used by Agent cannot block access" to ignored files. "Complete protection isn't guaranteed." | Yes | [Ignore file](https://cursor.com/docs/reference/ignore-file) |
| Hooks | `beforeShellExecution`, `beforeMCPExecution`, `beforeReadFile` can allow, ask, or deny. `postToolUse` can replace output for MCP tools only (`updated_mcp_tool_output`). `afterShellExecution` can only observe. | Yes (`C:\ProgramData\Cursor\hooks.json` for managed hooks) | [Hooks](https://cursor.com/docs/hooks) |
| Secret partners | 1Password hook checks that env files are mounted before shell commands run. | Partner | [Hooks](https://cursor.com/docs/hooks) |
| Network | First-party tools reach only GitHub, direct links, and web search by default. | Yes | [Agent security](https://cursor.com/docs/agent/security) |

## Codex

| Area | What it does | Native Windows? | Source |
|---|---|---|---|
| Native Windows sandbox | `elevated`: dedicated lower-privilege sandbox users, ACLs, firewall rules. `unelevated`: restricted token, ACLs, env-level offline. Private desktop by default. | Yes. Windows 11 recommended, Windows 10 1809+ best effort | [Windows sandbox](https://learn.chatgpt.com/docs/windows/windows-sandbox) |
| Sandbox limits | Blocks writes outside the workspace and network without approval. Commands can "read accessible files". | Yes | [Approvals and security](https://learn.chatgpt.com/docs/agent-approvals-security) |
| Deny reads | Permission profiles take `"deny"` for paths and globs such as `**/*.env`. Admins can force `permissions.filesystem.deny_read`. | Yes | [Config reference](https://learn.chatgpt.com/docs/config-file/config-reference) |
| Env policy | `shell_environment_policy` with `inherit`, filters, `set`. `ignore_default_excludes` defaults to `true`, so Codex does not remove `KEY`, `SECRET`, or `TOKEN` vars by default. | Yes | [Advanced config](https://learn.chatgpt.com/docs/config-file/config-advanced) |
| Network proxy | Optional domain allow and deny rules for command traffic. No credential inject or mask is documented. | Yes | [Approvals and security](https://learn.chatgpt.com/docs/agent-approvals-security) |
| Rules | Prefix rules (`allow`, `prompt`, `forbidden`) for commands that run outside the sandbox. "Rules are experimental." | Yes | [Rules](https://learn.chatgpt.com/docs/agent-configuration/rules) |
| Hooks | On by default. `PreToolUse` can block or rewrite input. `PermissionRequest` exists. `PostToolUse` `decision: "block"` replaces the tool result with the hook feedback. `updatedMCPToolOutput` is "not supported yet". `commandWindows` sets a Windows command. | Yes | [Hooks](https://learn.chatgpt.com/docs/hooks) |
| Platform | Microsoft names Codex as "integrating MXC" (Microsoft Execution Containers). | Preview | [Windows Developer Blog](https://blogs.windows.com/windowsdeveloper/2026/06/02/windows-platform-security-for-ai-agents/) |

## Windows platform features for agents

| Feature | What it does | Status | Source |
|---|---|---|---|
| Agent accounts and agent workspace | A separate standard account and desktop for an agent. Access to six known folders only, per agent "Allow Always / Ask every time / Never allow". | Experimental, Insider private preview. Built for Copilot Actions. Admin must turn it on. | [Support](https://support.microsoft.com/en-us/windows/ai/ai-features/experimental-agentic-features), [Security book](https://learn.microsoft.com/en-us/windows/security/book/operating-system-agentic-security) |
| Microsoft Execution Containers (MXC) SDK | Policy-driven process isolation and session isolation for agents. Micro-VM later. | Early preview after Build 2026 (2026-06-02). Adopters: GitHub Copilot CLI, Codex (integrating). Claude Code and Cursor are not named. | [Windows Developer Blog](https://blogs.windows.com/windowsdeveloper/2026/06/02/windows-platform-security-for-ai-agents/) |
| Agent identity | Windows gives an agent a local or Entra identity, so audit can tell agent from human. | Preview | [Build 2026 blog](https://blogs.windows.com/windowsdeveloper/2026/06/02/build-2026-furthering-windows-as-the-trusted-platform-for-development/) |
| Credentials and secrets | The agentic feature pages say nothing about Credential Manager, tokens, or secrets. | n/a | Same pages |

## Where CmdWarden still adds value

1. **Native Windows secret gate.** Claude Code and Cursor have no native Windows sandbox. Their mask and scrub features need WSL2.
2. **Stored tool tokens.** No harness gates the token that `gh`, `az`, `docker`, or `git` keeps in Credential Manager or a config file. A permitted `gh` command uses it silently.
3. **Tool plus launcher policy.** Harness rules match command text or a classifier guess. CmdWarden checks the real process chain and the account, and the same rule holds for every harness.
4. **Out-of-band approval.** Harness prompts show in the agent window. The Approval Gate is a separate Windows card with Windows Hello. It fails closed when no desktop is present.
5. **Fixed rules under classifier defaults.** Claude Code auto mode and Cursor Auto-review are now defaults. Both vendors say the classifier is not a hard boundary. CmdWarden gives a fixed deny that compaction cannot lose.
6. **Codex env gap.** Codex passes `KEY`, `SECRET`, and `TOKEN` vars to commands by default. `cw launch codex` removes them before the harness starts.
7. **Cursor shell output.** Cursor hooks cannot rewrite shell output. The CmdWarden MCP tool `run_with_secret` sends output through a path that Cursor lets a hook rewrite.
8. **Placeholder proxy on native Windows.** Claude Code masks credentials only inside its sandbox. `cw proxy` does the same job on native Windows for every harness.
9. **Canary tokens and cross-harness audit.** No harness offers canary tokens. No harness keeps one audit log across all agents.

## Where a harness already covers the job

1. **Claude Code in WSL2 or on macOS/Linux.** `sandbox.credentials` mask plus `injectHosts` does the job of `cw proxy`. `CLAUDE_CODE_SUBPROCESS_ENV_SCRUB` does part of the job of `cw launch`.
2. **Hiding secret files from the file tools.** Claude Code `Read` deny rules, Codex deny-read profiles, and `.cursorignore` cover `.env` reads by the file tools. The CmdWarden `beforeReadFile` leak guard adds little there.
3. **Command gates by text.** Claude Code `ask` rules, Codex `prompt` rules, and Cursor allowlists can make `gh pr create` ask. They match text only, but for a simple "ask first" they are enough.
4. **Codex writes and network.** The Codex elevated sandbox already stops writes outside the workspace and network use without approval.
5. **Leaked tokens in the transcript.** The Claude Code auto mode classifier blocks "printing a live credential". It is best effort, but it lowers the need for leak guard in that mode.
6. **Future: platform isolation.** MXC and agent accounts can later give each agent its own Windows identity. CmdWarden already keys policy by account (`cw policy enroll --account`), so it can use that identity as a launcher.

## Candidate features for CmdWarden v0.8.0

Ranked by the value test: a new user sees a real block in under 2 minutes, and keeps CmdWarden on after one week.

1. **Codex parity for hook, leak guard, and MCP.** Add `codex` to `cw hook install`, `cw leak-guard install`, and `cw mcp install`. Evidence: Codex hooks are on by default, support `commandWindows`, `PreToolUse` deny, and `PostToolUse` result replace.
2. **`cw setup <harness>`: one command to a first block.** Enroll, harden `gh`, install the hook, and plant a canary. Then print one prompt that the user pastes into the agent to see a real block. Evidence: auto modes are now defaults, so the CmdWarden card is often the only prompt the user sees.
3. **Write harness-native protections too.** For Claude Code, add `permissions.deny` `Read` rules for vaulted files and set `CLAUDE_CODE_SUBPROCESS_ENV_SCRUB=1` in `cw launch claude`. For Codex, set `shell_environment_policy.ignore_default_excludes = false` and exclude vaulted names. Evidence: both settings exist, and both are off by default.
4. **Harness posture in `cw doctor`.** Show per harness: sandbox on or off, Run Mode or permission mode, Codex `elevated` or `unelevated`, and the hooks in place. Evidence: each harness has a different weak default on Windows. A clear status helps the user keep CmdWarden on.
5. **Auto-enroll Codex sandbox accounts.** When `cw launch codex` or `cw doctor` finds `CodexSandboxOnline` or `CodexSandboxOffline`, offer to enroll them. Evidence: the Codex `elevated` sandbox runs commands as dedicated sandbox users.
6. **Cursor: steer secret commands to `run_with_secret`.** Make the Cursor `beforeShellExecution` deny message tell the agent to use the MCP tool. Evidence: Cursor can rewrite MCP output but not shell output.
7. **Spike: MXC and agent account identity as a launcher.** Do this when the MXC SDK leaves early preview. Evidence: Microsoft names Codex and Copilot CLI as MXC adopters.

## Sources

- Claude Code: [Sandboxing](https://code.claude.com/docs/en/sandboxing), [Permissions](https://code.claude.com/docs/en/permissions), [Permission modes](https://code.claude.com/docs/en/permission-modes), [Hooks](https://code.claude.com/docs/en/hooks), [Env vars](https://code.claude.com/docs/en/env-vars), [Security](https://code.claude.com/docs/en/security), [Setup](https://code.claude.com/docs/en/setup), [Tools reference](https://code.claude.com/docs/en/tools-reference), [CHANGELOG](https://github.com/anthropics/claude-code/blob/main/CHANGELOG.md)
- Cursor: [Run Modes](https://cursor.com/docs/agent/security/run-modes), [Agent security](https://cursor.com/docs/agent/security), [Hooks](https://cursor.com/docs/hooks), [Ignore file](https://cursor.com/docs/reference/ignore-file), [Terminal](https://cursor.com/docs/agent/tools/terminal), [Sandbox blog](https://cursor.com/blog/agent-sandboxing)
- Codex: [Windows sandbox](https://learn.chatgpt.com/docs/windows/windows-sandbox), [Approvals and security](https://learn.chatgpt.com/docs/agent-approvals-security), [Advanced config](https://learn.chatgpt.com/docs/config-file/config-advanced), [Config reference](https://learn.chatgpt.com/docs/config-file/config-reference), [Rules](https://learn.chatgpt.com/docs/agent-configuration/rules), [Hooks](https://learn.chatgpt.com/docs/hooks), [Releases](https://github.com/openai/codex/releases)
- Windows: [Experimental agentic features](https://support.microsoft.com/en-us/windows/ai/ai-features/experimental-agentic-features), [Security book: agentic security](https://learn.microsoft.com/en-us/windows/security/book/operating-system-agentic-security), [Windows platform security for AI agents](https://blogs.windows.com/windowsdeveloper/2026/06/02/windows-platform-security-for-ai-agents/), [Build 2026 for developers](https://blogs.windows.com/windowsdeveloper/2026/06/02/build-2026-furthering-windows-as-the-trusted-platform-for-development/)
