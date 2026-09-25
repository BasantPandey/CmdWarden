# CmdWarden - Product and Architecture Specification

**Status:** Handoff-ready (wayfinder map complete)  
**Product:** CmdWarden  
**CLI:** `cw` (alias `cmdwarden`)  
**Stack:** C# / .NET, Windows-native  
**Map:** [CmdWarden product handoff (beyond spike)](https://github.com/BasantPandey/CmdWarden/issues/12)

This document consolidates standing decisions, research, and resolved wayfinder tickets so implementation can proceed without reopening what the product is. Domain language lives in [the Glossary](../glossary.md). Research notes and prototypes are no longer in the repo. Find them in git history before commit `14e5e6b` under `docs/research/` and `docs/prototypes/`.

---

## 1. Purpose and positioning

CmdWarden is a **Windows security layer** that gates CLI secret use by **tool** and **Launcher** identity so AI harnesses and supply-chain code cannot freely read or exfiltrate credentials.

It is an **inspired twin** of macOS Automic Vault's *job* (vault + tool×launcher gate + scan/harden + approvals + audit) - not a CLI clone, not a shared codebase, not multi-OS monorepo with upstream.

**First user:** this developer machine. **First catalog:** `gh`, `git`, `az`, `docker`. **Spike vertical:** end-to-end `gh` (PATH shim → Session Agent → identity → vault → Approval Gate → real `gh`).

---

## 2. Standing architecture decisions

| Area | Decision |
|------|----------|
| Product scope | Full core twin (vault, gate, scan/harden, approvals, audit) - not agent-only |
| Shell | CLI (`cw` / `cmdwarden`) + per-user **Session Agent** |
| Agent host | Per-user interactive session process - **not** Session 0 Windows Service |
| Vault | Windows Credential Manager, `CRED_TYPE_GENERIC`, product-prefixed targets |
| Identity | Hybrid L3: Authenticode + process chain preferred; path+SHA-256 fallback; unknown fails closed for auto-approve |
| Harden | **PATH shims only** - no API hooking / detours |
| IPC | gRPC over Windows named pipes; CurrentUserOnly ACL |
| Approvals | WinUI 3 card (out-of-process helper); MessageBox transitional/CI; phone/OOB out of scope; UI detail: [approval-gate-ui.md](./approval-gate-ui.md) |
| Policy levels | Deny / Read / Trusted / Full |
| Defaults | AI Harness → Read; Terminal → Trusted; unknown/unenrolled → Deny |
| Command classes | read / write / secret-reveal / unknown (unknown auto-allows only under Full) |

---

## 3. Components

```text
┌─────────────┐     named pipe gRPC      ┌──────────────────┐
│ cw / shim   │ ───────────────────────► │ Session Agent    │
│ (client)    │                          │ - identity       │
└─────────────┘                          │ - policy         │
                                         │ - vault (CredMan)│
                                         │ - approval UI    │
                                         │ - audit write    │
                                         └──────────────────┘
                                                  │
                         shims early on user PATH │
                                                  ▼
                                         real tool (absolute path)
```

| Component | Responsibility |
|-----------|----------------|
| **CmdWarden.Contracts** | Domain types, protos, channel factory, policy keys |
| **CmdWarden.Agent** | Session Agent host: gRPC, identity, vault, policy load, Approval Gate, audit |
| **CmdWarden.Cli** | `cw` / `cmdwarden` - doctor, vault, policy, agent, audit, scan, harden |
| **Shims** | Multiplexed or per-tool early PATH stand-ins; ask agent; spawn real tool |

**Product root (state):** `%LOCALAPPDATA%\CmdWarden\`  
- `shims\` - PATH prepend only this dir  
- `policy.json` - enrollment and levels  
- `audit\` - NDJSON gate log  
- Harden pins (absolute real path + hash per tool)

**Tool package** (dotnet tool) holds binaries only - not durable state.

---

## 4. Session Agent and IPC

- Listens on named pipe `CmdWarden-<UserName>` (override `CW_PIPE_NAME`).
- **CurrentUserOnly** pipe ACL.
- Surfaces: health, caller identity, save/release/delete secret (expand to Authorize for shims as spike continues).
- **Lazy start:** first `cw` or shim call starts agent if down; also `cw agent start|stop|status`.
- Approval mode: default/`prompt` = process helper card; `messagebox` = transitional MessageBox; tests: `CW_APPROVAL_MODE=allow|deny|off`.

Research: session-agent-ipc.md.

---

## 5. Identity (Launcher)

On each request:

1. Resolve client PID from named pipe (OS-bound).
2. Walk parent chain; populate Authenticode or path+hash per node.
3. Select policy launcher (skip agent/cli/dotnet as appropriate).
4. **Auto-approve eligible** only if authenticode or pathhash and not unknown/pid-reuse.

**Policy keys:** `auth:sha1:<thumbprint>` | `pathhash:sha256:<hex>` | `unknown`.

Research: windows-launcher-identity.md.

---

## 6. Policy and enrollment

### Levels × classes (auto-allow matrix)

| Level \ Class | read | write | secret-reveal | unknown |
|---------------|------|-------|---------------|---------|
| **Deny** | no | no | no | no |
| **Read** | yes | no | no | no |
| **Trusted** | yes | yes | no | no |
| **Full** | yes | yes | yes | yes |

If not auto-allow → **Approval Gate** (user: Approve Once / Deny; domain: Allow once / Deny). If UI unavailable → **fail closed** (block).

### Enrollment (v1)

- **Explicit only** - no silent auto-enroll.
- CLI: `cw policy enroll --kind terminal|ai-harness` [`--key`], `list`, `set`, `unenroll`, `path`, `sessions`.
- Bind **policy key**; store display path for list UX.
- Key change (new hash/thumbprint) → **fail closed**, re-enroll required.
- Kinds only: `ai_harness` (default Read), `terminal` (default Trusted).

Issue: [launcher enrollment UX](https://github.com/BasantPandey/CmdWarden/issues/17).

---

## 7. Vault

- Win32 Cred* GENERIC under targets such as `CmdWarden/secret/<NAME>`.
- Release only after policy (and optional Approval Gate); audit write first (**fail closed** if audit fails).
- Short-lived materialization into **child process only** for inject/shim (never leave secrets in parent env).
- **Credential helper gate ([#202](https://github.com/BasantPandey/CmdWarden/issues/202)):** `HelperCredential { tool, action, server_url, username, secret }` serves `docker` registry credentials from `CmdWarden/docker/<ServerURL>`.
  - The Agent derives the vault name and the class: `get` and `list` are read; `store` and `erase` are write.
  - Chain rule: the pinned, signed real tool sits directly above the helper, or one docker plugin (`docker-compose.exe`, `docker-buildx.exe`) sits between them. Any other chain denies `HelperParentMissing`.
  - The launcher resolver skips a file under `shims/`, a pinned tool, and a plugin directly below a pinned tool. A plugin name alone does not count.
  - A live shim grant (`Authorize`) covers helper reads from its chain with reason `RunCovered`. It never covers `store` or `erase`. Policy edits and re-pins clear it.
  - Every action writes a gate row with the URL as the secret name and no value. Docker entries never appear in the Secrets tab.
  - Helper binary ([#203](https://github.com/BasantPandey/CmdWarden/issues/203)): `docker-credential-cmdwarden.exe` ships in `shim-payload/` and `cw harden docker` copies it to `shims/`. Protocol: one argv action, stdin payload, protocol JSON on stdout. Not found prints `credentials not found in native keychain` and exits 1. A deny prints `CmdWarden: docker registry credential denied (<reason>)` and exits 1. `list` prints URL to username only.
  - Strong mode ([#204](https://github.com/BasantPandey/CmdWarden/issues/204)): `cw harden docker --strong` reads CredMan for `Docker Credentials` entries and inline `auths`, saves each to `CmdWarden/docker/<ServerURL>` if absent or equal, erases the legacy entries, then writes `config.json` atomically with `credsStore: cmdwarden`. A different vault value or an entry over 2560 bytes fails the whole harden before any change. A failure before the config write puts the originals back and removes the vault copies. The pin document records `mode: strong` and the previous `credsStore`; the shim grant then carries no `DOCKER_AUTH_CONFIG`. Doctor and the Hardened Tools tab report Degraded on `credsStore` drift or a returned legacy entry; re-harden repairs. `cw unharden docker` writes the entries back in wincred layout, deletes `CmdWarden/docker/*`, restores `credsStore`, and removes the pin, shim, and helper.
  - Strong git ([#207](https://github.com/BasantPandey/CmdWarden/issues/207)): `cw harden git --strong` reads CredMan for `<namespace>:*` entries (`credential.namespace`, default `git`), saves each to `CmdWarden/git/<key>` if absent or equal, erases the legacy entries, then writes the global config through `git config --global`: `--replace-all credential.helper ""` and `--add` the helper path in `sh` form. It fails closed before any write when `credential.credentialStore` is `dpapi` or `plaintext` or when `~\.git-credentials` exists. The previous global `credential.helper` values and every gh host-scoped helper block are saved in the pin document and removed; the system file stays. In strong mode the grant strips `GIT_CONFIG_GLOBAL`, `GIT_CONFIG_SYSTEM`, and `GIT_CONFIG_NOSYSTEM` from the child env, and a `credential.*` key in `GIT_CONFIG_KEY_<n>` classifies like `-c`. Doctor reports Degraded on helper chain drift, a returned gh block, or a returned `<namespace>:` entry; re-harden repairs. `cw unharden git` restores the config lines and gh blocks, writes the entries back in GCM layout, deletes `CmdWarden/git/*`, and removes the pin, shim, and helper.
  - Strong gh store ([#208](https://github.com/BasantPandey/CmdWarden/issues/208)): the vault holds `CmdWarden/gh/<host>` for the active slot and `CmdWarden/gh/<user>@<host>` per user; the active user per host comes from `hosts.yml`; entries are hidden from the Secrets tab. When the gh pin says `mode: strong` the Agent reads `CmdWarden/gh/*` only and ignores the compat `GH_TOKEN`. Injection by host class: `GH_TOKEN` always for the github.com slot; `GH_ENTERPRISE_TOKEN` when the vault holds one GHES slot, or when argv `--hostname`, `-R host/owner/repo`, or env `GH_HOST` names a vaulted GHES slot; two GHES slots and no named host inject none. Each released entry writes its own audit row with the vault name. `MigrateToolStore { tool=gh, argv }` is served only in strong mode from a live granted run: login and refresh enumerate `gh:<host>:<user>` and `oauth_token` lines, verify each with the real `gh auth status --hostname`, save-if-absent-or-equal, delete the stock entries, and strip `oauth_token`; logout drops the vault entries `hosts.yml` no longer lists. The shim calls it when the grant says `migrate_after_run` and the child exits 0.
  - Strong gh harden ([#209](https://github.com/BasantPandey/CmdWarden/issues/209)): `cw harden gh --strong` runs the same migration as `MigrateToolStore` with the pinned real `gh`; a different vault value or a failed verify fails the whole harden before any delete. No `gh auth logout`. The pin document records `mode: strong` and the host and user list; the compat `GH_TOKEN` import is skipped. `--token` with `--strong` verifies and writes `CmdWarden/gh/<host>` for `--hostname` (default `github.com`). Doctor reports Degraded on a returned `gh:<host>:*` entry or `oauth_token` line ("stock gh token returned") and on a host whose active user has no vault token ("no vault token for <host>"); re-harden repairs the first two. The Hardened Tools row and `cw harden --list` show `Hardened (strong - N hosts, M accounts in vault)`. `cw unharden gh` writes every entry back in stock layout, writes the active user's token to the `gh:<host>:` slot, deletes `CmdWarden/gh/*`, leaves the compat `GH_TOKEN`, and removes the pin and shim. Residual: real `gh auth switch`, and a logout that switches the active user, read the stock keyring and fail in strong mode; log in again through the shim instead.

Research: credential-manager-vault.md.

### Vault secrets manager UI

**UI source of truth:** [vault-secrets-ui.md](./vault-secrets-ui.md) (map [#83](https://github.com/BasantPandey/CmdWarden/issues/83)).

Summary for architecture readers:

- **Chrome:** Start-menu WPF window on `net10.0-windows` (`CmdWarden Vault`; the brief targeted WinUI 3, shipped as WPF to match the Approval Gate, see winui-secrets-manager-hosting.md); single-column name list + toolbar (Add / Refresh / Delete); accepted prototype under `docs/prototypes/vault-secrets-manager.*`.
- **Host:** **Separate** unpackaged process, gRPC client of the Session Agent over named pipes -- never in-process with the agent or shared with the Approval Gate helper.
- **Surface:** secret **names only**; never values. Add modal (name + masked value); overwrite confirm-replace; delete confirm-by-name. Agent-down banner disables actions.
- **Agent contract:** new `ListSecretNames` RPC (names only via `CredEnumerateW` filter); reuses `SaveSecret` / `DeleteSecret`; no Approval Gate on list.
- **Install:** per-user `.lnk` shortcut under `%APPDATA%\Microsoft\Windows\Start Menu\Programs\`.
- **Non-goals:** inject/reveal from UI; Windows Hello; full management app; always-on tray.

Issues: [Vault secrets UI map](https://github.com/BasantPandey/CmdWarden/issues/83).

---

## 8. Approval Gate

**UI source of truth:** [approval-gate-ui.md](./approval-gate-ui.md) (map [#68](https://github.com/BasantPandey/CmdWarden/issues/68)).

Summary for architecture readers:

- **Chrome:** Automic-style **Deny / Approve Once** card, Windows-native layout (accepted prototype under `docs/prototypes/approval-gate-card.*`). Native caption uses the **Lintel** product mark via `ApplicationIcon`; in-window header is compact mark + `CmdWarden` (8px gap). Detail: [approval-gate-ui.md](./approval-gate-ui.md) (Window chrome).
- **Host:** **Out-of-process** Fluent-style helper under `agent/approval-gate/` (default interactive). MessageBox via `CW_APPROVAL_MODE=messagebox` only. Missing helper → **Unavailable** (fail closed).
- **Surface:** native title bar (Lintel + `CmdWarden`); compact in-window brand row; launcher icon + display name + `wants to run`; command block (invocation, tool path, `cwd`, secret **names**); reason line; Details expander (collapsed); **Deny** / **Approve Once**.
- **Never:** secret **values**; Always Approve on dialog; full process chain on main surface.
- **Outcomes:** Approve Once → `AllowOnce`; Deny → `Deny`; cannot show / timeout → `Unavailable` → **fail closed**.
- **Allow for session ([#132](https://github.com/BasantPandey/CmdWarden/issues/132)):** third button, shown only for enrolled launchers and never for secret-reveal; Approve Once stays the default. Grants the launcher process (pid + start time) the approved class and lower for tool + secret until it exits or is idle 60 minutes (`CW_SESSION_IDLE_SECONDS`). Granting call is audited `session-grant`; covered calls `session-allow` with reason `SessionAllow`. Helper exit code 3; scripted mode `CW_APPROVAL_MODE=session`.
- **Deny cooldown:** after a Deny, the same launcher process gets no new popup for that tool for 2 minutes (`CW_DENY_COOLDOWN_SECONDS`). Other arguments do not open a new popup. Each blocked call is audited `deny` with reason `DenyCooldown`. Only one popup shows at a time. A waiting call checks the cooldown before its popup opens.
- **Approve Once lasts the session ([#205](https://github.com/BasantPandey/CmdWarden/issues/205)):** an Approve Once click grants the launcher process the **one class it showed** for tool + secret, on the same terms as Allow for session (enrolled launcher, never secret-reveal, launcher exit or 60 idle minutes). A higher class prompts again. A later decision **adds** to the live grant, so a narrow answer never takes coverage away. Covered calls audit `session-allow`; the granting call still audits `allow-once`.
- **Real input only ([#23](https://github.com/BasantPandey/CmdWarden/issues/23)):** Approve Once and Allow for session accept only keyboard or mouse input that the helper's low-level hooks saw without the injected flag. `SendInput`, `PostMessage`, and UI Automation `Invoke` get no answer, and the popup shows `Use your keyboard or mouse.` Deny accepts every input type.
- **Approval binds the files ([#30](https://github.com/BasantPandey/CmdWarden/issues/30)):** `cw inject` resolves the program on PATH (with PATHEXT) and finds the script its interpreter runs (`bash x.sh`, `pwsh -File x.ps1`, `python x.py`, `node x.js`, `cmd /c x.cmd`; inline `-c` / `-Command` / `-e` has no file). It locks those files against writes and deletes until the child exits, and sends their paths as `bound_paths`. The Agent hashes them at approval time and returns `bound_files`; `cw inject` checks the hashes again just before the start and does not run on a mismatch. The transient key includes the hashes, and a session grant covers only the files and hashes a person approved. A changed file opens a new popup with the heading `Script changed after approval`, and the decision row gets reason `ScriptChanged`. Each shim locks the pinned binary and checks `real_sha256` just before the start; the lock lasts the run, so a pinned `az.cmd` cannot change while `cmd` reads it.
- **Hidden PowerShell ([#31](https://github.com/BasantPandey/CmdWarden/issues/31)):** on `Authorize` and `ReleaseSecret`, the Agent reads the command line of each `pwsh.exe` / `powershell.exe` in the caller chain, up to and with the launcher, and parses it with the PowerShell parser (`System.Management.Automation.Language`). Opaque: `-EncodedCommand` (any accepted prefix), `-Command -` (stdin), `Invoke-Expression` / `iex`, `&` or `.` on a name built at run time, `[ScriptBlock]::Create`, `InvokeScript` / `NewScriptBlock` / `ExpandString`, a method name built at run time, a nested PowerShell with an opaque part, code that does not parse, and a `-File` script that cannot be read or holds any of these. An opaque part forces the Approval Gate whatever the policy level. The card heading is `Hidden PowerShell command` with the reason, no transient or session answer covers the call, the card offers no session, and the decision row gets reason `HiddenCommand`. A plain `gh pr list` keeps its normal policy.
- **Windows Hello ([#24](https://github.com/BasantPandey/CmdWarden/issues/24)):** after a real Approve, the popup asks for Windows Hello (fingerprint, face, or PIN) through `UserConsentVerifier` when the policy says so. `policy.json` field `hello`, set with `cw policy hello off|secret-reveal|write-and-up` (default `secret-reveal`), shown by `cw policy list`. Verified → the approve stands, audit reason `HelloVerified`. Cancel, retries exhausted, or device busy → Deny, audit reason `HelloCanceled`. No Hello device, not set up, or disabled by policy → the plain popup decides, audit reason `HelloUnavailable`. The helper adds a flag to its exit code: `0x10` verified, `0x20` unavailable (on an approve), `0x40` cancelled (on a deny). Any other combination fails closed.
- **CI:** scripted modes via `CW_APPROVAL_MODE`.
- **Transient reuse ([#131](https://github.com/BasantPandey/CmdWarden/issues/131)):** a human `AllowOnce` / `Deny` is reused, in memory only, for an exact retry (same launcher pid + start time, tool, class, secret name, command line) while the launcher process lives; `CW_TRANSIENT_REUSE_SECONDS` adds an optional time cap; audited with reason `TransientReuse`. Policy auto-allow and `Unavailable` are recomputed every call.
- **Invalidation ([#133](https://github.com/BasantPandey/CmdWarden/issues/133)):** all memory clears on agent stop and on workstation lock (`SessionSwitch` / `SessionLock`). `cw policy set` clears entries whose launcher key or tool matches; `cw policy unenroll` clears that launcher key; a `cw harden <tool>` that re-pins the binary clears that tool. Cleared through the existing policy-store and pin-store call sites - no polling.
- **Visibility / revocation ([#134](https://github.com/BasantPandey/CmdWarden/issues/134)):** `cw policy sessions` lists active session allows (id, launcher key + kind, pid, tool, secret name, class, granted / last-used / idle-expiry); `--revoke <id>` / `--revoke-all` withdraw them and print the count. Backed by `ListSessionAllows` / `RevokeSessionAllow` RPCs - names only, no prompt. Transient entries are never listed. The Secret Gates tab mirrors the list read-only under the Defaults card ([#135](https://github.com/BasantPandey/CmdWarden/issues/135)).

Issues: [Approval Gate UI map](https://github.com/BasantPandey/CmdWarden/issues/68), [Windows Hello / step-up](https://github.com/BasantPandey/CmdWarden/issues/24).

---

## 8a. Leak guard and canary tokens

**Leak guard ([#27](https://github.com/BasantPandey/CmdWarden/issues/27)).** The Session Agent holds the real values, so it finds a leak with no guess.

- `CheckLeak { texts, source }` returns each text with every CmdWarden vault value (`CmdWarden/*`, at least 8 characters) replaced by `[CmdWarden: NAME]`, plus the matched names. Values never leave the Agent.
- Each match writes an audit row: decision `redact`, reason `LeakRedacted`, tool `leak-guard`, the secret name, the launcher, and the source as purpose.
- `cw leak-guard install claude` adds a Claude Code `PostToolUse` hook (matcher `*`) to `~/.claude/settings.json`. The hook sends every string of `tool_response` in one call and returns `updatedToolOutput` with the same shape, so the model sees only the placeholder.
- `cw leak-guard install cursor` adds `beforeReadFile`, `postToolUse`, and `afterShellExecution` to `~/.cursor/hooks.json`. Cursor can block a read (`permission: deny`) and replace MCP output (`updated_mcp_tool_output`). It cannot change shell output, so a shell match only adds a note for the model.
- When the Agent is down, the hook lets the output pass and shows a note. It never blocks the harness.
- Residual: only exact values; an encoded copy (base64, URL encoding) passes.

**Canary tokens ([#29](https://github.com/BasantPandey/CmdWarden/issues/29)).** A fake token that no normal work uses is a clear sign of an attack.

- `cw canary install [--env <file>]...` saves `GH_TOKEN_BACKUP` in the vault, appends a `[backup-admin]` profile to `~/.aws/credentials`, and appends `GITHUB_TOKEN=` to each named .env template. `canaries.json` under the product root records each value and the exact text added.
- The Agent treats a canary as used when: a leak guard text holds a value, a shim argv holds a value, the SHA-256 of a shim caller env value matches (`env_value_hashes`, never values), or `ReleaseSecret` asks for the canary vault entry or names a value on its command line.
- On a use: the Agent clears every grant and remembered answer of that launcher key, blocks the launcher process for every tool (default 1 hour, `CW_CANARY_COOLDOWN_SECONDS`, or until the launcher restarts), writes a `deny` row with reason `CanaryHit`, and shows an alarm card in the lower right corner (interactive gate only).
- `cw canary remove` takes out the exact text it added (or each line with a canary value, when someone edited it), deletes the vault entry when it still holds the fake value, and empties `canaries.json`. `cw canary status` lists the places.

## 9. Audit

| Item | Spec |
|------|------|
| Location | `%LOCALAPPDATA%\CmdWarden\audit\` |
| Format | NDJSON (one JSON object per line) |
| Events | **Gate decisions only** (auto-allow, allow-once, deny, unavailable, session-grant, session-allow, redact) |
| Fields | ts, decision, reason_code, tool, command_class, policy_level, launcher_policy_key, launcher_kind, enrollment_kind, secret_name; optional purpose/path/pid |
| Never log | Secret **values**, full argv, env dumps |
| Retention | ~**30 days**, prune older |
| Access | `cw audit` |
| Fail closed | No secret release if audit cannot be written |

Issue: [audit log storage and retention](https://github.com/BasantPandey/CmdWarden/issues/18).

---

## 10. Install and distribution

| Item | Spec |
|------|------|
| Vehicle | **dotnet tool** (global, per-user) |
| Package | **One package**: CLI + Session Agent, same version |
| Scope | Per-user only - no admin / machine-wide v1 |
| Feed | Local/CI nupkg for handoff; nuget.org deferred |
| Update | `dotnet tool update -g` |
| Install vs harden | Install = **binaries only**; harden/setup adds Path + shims |

Issue: [install and distribution](https://github.com/BasantPandey/CmdWarden/issues/16).

---

## 11. CLI surface (v1)

`cw` / `cmdwarden`:

| Command | Role |
|---------|------|
| help, version | Meta |
| doctor | Agent health / pipe |
| whoami / identity | Launcher identity |
| agent start \| stop \| status | Explicit agent control |
| save, inject, delete | Vault |
| policy list \| enroll \| set \| unenroll \| path \| sessions | Enrollment, levels, and session-allow list/revoke |
| audit | Tail gate log |
| scan | First-catalog detectors |
| harden \<tool\> | Opt-in shim + pin for catalog tools |
| launch \<harness\> | Start Claude Code, Codex, or Cursor without ambient token variables ([#25](https://github.com/BasantPandey/CmdWarden/issues/25)) |

**Primary journey:** install → doctor → enroll → harden gh → day-to-day shim use; plus scan / audit / manual vault.

Issue: [v1 cw CLI command surface](https://github.com/BasantPandey/CmdWarden/issues/21).

---

### cw launch ([#25](https://github.com/BasantPandey/CmdWarden/issues/25))

- Catalog: `claude` (Claude Code, `claude.exe`), `codex` (Codex, `codex.exe`), `cursor` (Cursor, `Cursor.exe`).
- The harness binary is the process that starts shells, so it is the launcher CmdWarden sees. A native install on PATH is that binary. For an npm `.cmd` shim, `cw launch` starts the shim and looks for the binary in the npm package the shim names. Cursor starts from `%LOCALAPPDATA%\Programs\cursor\Cursor.exe`.
- The child environment is a copy without `AmbientEnv.All`, the list the scan detectors use: `GH_TOKEN`, `GITHUB_TOKEN`, `GH_ENTERPRISE_TOKEN`, `GITHUB_ENTERPRISE_TOKEN`, `GH_PATH`, `AZURE_CLIENT_SECRET`, `AZURE_CLIENT_CERTIFICATE_PATH`, `AZURE_FEDERATED_TOKEN_FILE`, `DOCKER_AUTH_CONFIG`. `cw launch` prints the names it removed, never values.
- When the binary's policy key has no enrollment, `cw launch` enrolls it as `ai_harness`. An existing enrollment stays as it is.
- A running Cursor gets a new window in the old process, with the old environment; `cw launch cursor` says so.
- `cw shortcut install` adds `<Harness> (CmdWarden).lnk` to the Start Menu for each harness on this PC; `cw shortcut remove` deletes them.

## 12. Scan engine

- **Coded C# detectors** in product (versioned with tool) - not downloadable rule packs.
- Structured findings: id, tool, severity, title, summary, evidence (no secrets), remediation / harden_hint.
- **`cw scan`** on demand; first catalog (`gh`/`git`/`az`/`docker`) + light system checks.
- Read-only; hints at `cw harden …`; **no** auto-harden; **no** FS watcher in v1.
- **MCP and harness config ([#28](https://github.com/BasantPandey/CmdWarden/issues/28)):** `mcp.plain_secret` reads `~/.claude.json` (top level and per project), `~/.claude/settings.json`, `~/.cursor/mcp.json`, Claude Desktop `claude_desktop_config.json`, VS Code `mcp.json`, and in the working folder `.mcp.json`, `.claude/settings*.json`, `.cursor/mcp.json`, `.vscode/mcp.json`. A finding is an `env` or `headers` value with a known token prefix (`ghp_`, `github_pat_`, `sk-`, `xoxb-`, `AKIA`, and others), a `Bearer` or `token` header, or a secret-like key name (`TOKEN`, `SECRET`, `API_KEY`, ...). Placeholders (`${VAR}`, `<...>`, `[CmdWarden: ...]`) do not count. Evidence names the file and the key, never the value.
- **Move to vault:** for an `env` value of a server that starts a command, `ScanFinding.Fix` carries the location. `cw scan --move-to-vault` and the **Move to vault** button on the Detectors page save the value as vault secret `<KEY>` (a different existing value stops the move before any change), set the file value to `[CmdWarden: <KEY>]`, and wrap the server as `cw inject --tool mcp --class read +<KEY> -- <command> <args>`. The server gets the value at start from the vault; `cw policy set <key> mcp <level>` controls it. Headers and remote servers get the finding without an automatic fix. `cw scan --move-to-vault` stops when Claude Code runs, because Claude Code rewrites `~/.claude.json`.

Issue: [scan engine shape](https://github.com/BasantPandey/CmdWarden/issues/19).

---

## 13. PATH shims (common)

Research: path-shim-patterns.md.

- Per-user shim dir on **user Path** (prepend).
- Shim first on PATH ([#201](https://github.com/BasantPandey/CmdWarden/issues/201), [#210](https://github.com/BasantPandey/CmdWarden/issues/210)): `HardenedToolStatus.Probe` composes machine then user registry entries and reports Degraded when an earlier entry holds `<tool>.exe|cmd|bat`. `cw harden` never elevates. `cw doctor --fix-path` runs `cw doctor --fix-path --elevated <entry>` once under UAC; that run prepends `%LOCALAPPDATA%\CmdWarden\shims` to the machine PATH as `REG_EXPAND_SZ` (idempotent, vendor entries untouched) and broadcasts `WM_SETTINGCHANGE`. Doctor then reads the logon environment block (`CreateEnvironmentBlock` for the current user); when the block does not expand the entry, a second elevated run writes the literal shims path instead. On success doctor prints `shims first on PATH (machine)` and re-probes. A non-default product root always uses the literal path.
- Prefer multiplexed signed shim; tool id from image name.
- Harden time: absolute real path + SHA-256 (+ optional Authenticode).
- Runtime: **full-path spawn only** - never re-search PATH for the real tool.
- Residual (all tools): absolute path bypass of PATH harden; same-user tamper.

---

## 14. First catalog harden designs

All **compat mode by default**. Strong mode is opt-in per tool with `--strong` for `gh`, `git`, and `docker` (map [#185](https://github.com/BasantPandey/CmdWarden/issues/185), plan first-catalog-strong-mode-plan.md). Strong mode moves the stock store into the vault and deletes the originals; `cw unharden` writes them back. `az` has no strong mode ([#157](https://github.com/BasantPandey/CmdWarden/issues/157)).

### 14.1 gh

Research: gh-windows-harden.md. Issue: [#22](https://github.com/BasantPandey/CmdWarden/issues/22).

| Item | Design |
|------|--------|
| Store | CredMan `gh:host` |
| Harden | Pin real `gh.exe`; PATH shim; **import active token** into CmdWarden vault |
| Runtime | On allow: child **always** gets `GH_TOKEN` from vault |
| Classes | export (`auth token`, show-token, git-credential get) → **secret-reveal**; side effects / most auth → **write**; read-mostly → **read**; unmatched → **unknown** |
| Strong | `cw harden gh --strong` ([#208](https://github.com/BasantPandey/CmdWarden/issues/208), [#209](https://github.com/BasantPandey/CmdWarden/issues/209)): stock `gh:<host>:<user>` entries and `oauth_token` lines move to `CmdWarden/gh/*` and are deleted; per-host `GH_TOKEN` / `GH_ENTERPRISE_TOKEN` injection; `auth login` / `refresh` / `logout` / `switch` get no token env and migrate after exit 0 |
| Residual | Absolute path; leftover `gh:` keyring (compat only); parent ambient env; `gh auth switch` fails in strong mode |

Spike implement shipped: issues #7 (shim pin), #8 (harden gh), #9 (classifier table), #10 (audit wiring).

### 14.2 git

Research: git-windows-harden.md. Issue: [#23](https://github.com/BasantPandey/CmdWarden/issues/23).

| Item | Design |
|------|--------|
| Store | GCM / `git:https://…` CredMan (typical) |
| Harden | Pin real `git.exe`; PATH shim; **leave GCM** in place |
| Runtime | **No** GH_TOKEN-style inject; gate process only; allowed ops still use ambient GCM |
| Classes | `credential fill` → **secret-reveal**; `push` → **write**; `fetch`/`clone`/local read → **read** |
| Strong | `cw harden git --strong` ([#205](https://github.com/BasantPandey/CmdWarden/issues/205), [#206](https://github.com/BasantPandey/CmdWarden/issues/206), [#207](https://github.com/BasantPandey/CmdWarden/issues/207)): GCM entries move to `CmdWarden/git/*` and are deleted; `git-credential-cmdwarden.exe` is the only global `credential.helper`; the Agent checks the signer walk up to the pinned `git.exe`; `store` equal is a no-op, `erase` needs an equal value |
| Residual | Absolute path; IDE git; allowed AI push uses full GCM tokens (compat only); SSH; WSL |

**Independent of gh harden** - both required for first catalog.

### 14.3 az

Research: az-windows-harden.md. Issue: [#24](https://github.com/BasantPandey/CmdWarden/issues/24).

| Item | Design |
|------|--------|
| Store | MSAL DPAPI under `~\.azure` (not CredMan) |
| Harden | Pin real `az.cmd` (+ python); shim `az.cmd`/`az.exe`; leave MSAL cache |
| Runtime | No user-token env inject; gate only |
| Classes | `account get-access-token` → **secret-reveal**; login/CRUD → **write**; list/show → **read** |
| Strong | None. MSAL isolation and ambient env strip deferred ([#157](https://github.com/BasantPandey/CmdWarden/issues/157)) |
| Residual | Absolute path; same-user DPAPI; SP env; Azure PowerShell out of scope |

### 14.4 docker

Research: docker-windows-harden.md. Issue: [#25](https://github.com/BasantPandey/CmdWarden/issues/25).

| Item | Design |
|------|--------|
| Store | Desktop `credsStore` / wincred; optional base64 `auths` |
| Harden | Pin real `docker.exe`; PATH shim; leave Desktop store |
| Runtime | Optional **child `DOCKER_AUTH_CONFIG`** from vault on allow; never leave in parent |
| Classes | login/push → **write**; pull → **read** |
| Helpers | `docker-credential-cmdwarden.exe` gates registry credentials when `credsStore` is `cmdwarden` ([#203](https://github.com/BasantPandey/CmdWarden/issues/203)); the pinned `docker.exe` must sit at depth 1, or at depth 2 behind compose or buildx |
| Strong | `cw harden docker --strong` ([#204](https://github.com/BasantPandey/CmdWarden/issues/204)): `Docker Credentials` entries and inline `auths` move to `CmdWarden/docker/*` and are deleted; `credsStore: cmdwarden`; no `DOCKER_AUTH_CONFIG` overlay; foreign `credHelpers` stay with a warning |
| Residual | Absolute path; foreign `credHelpers`; ambient env; WSL |

---

## 15. Threat notes (summary)

| Threat | Mitigation |
|--------|------------|
| AI harness dumps tokens (`gh auth token`, `git credential fill`, `az account get-access-token`, helper get) | Command class secret-reveal + policy (AI default Read) + Approval Gate / block |
| Ambient long-lived env tokens | Child-only inject where applicable; scan profiles; `cw launch <harness>` starts the harness without them ([#25](https://github.com/BasantPandey/CmdWarden/issues/25)) |
| Same-user CredRead / DPAPI | Agent policy is the gate; strong mode empties the stock store for `gh` / `git` / `docker`; residual accepted in compat and for `az` |
| Absolute path bypass of PATH shim | Scan + guidance; WDAC out of PATH-only v1 |
| Secret values in tool output read by the model | Leak guard hooks replace each vaulted value with `[CmdWarden: NAME]` ([#27](https://github.com/BasantPandey/CmdWarden/issues/27)) |
| Prompt injection that hunts for tokens | Canary tokens raise an alarm and block the launcher ([#29](https://github.com/BasantPandey/CmdWarden/issues/29)) |
| Script or binary changed after approval (TOCTOU) | Approval binds file hashes; files stay locked until the child exits ([#30](https://github.com/BasantPandey/CmdWarden/issues/30)) |
| Wrapper bypass | PowerShell wrappers in the caller chain are parsed; hidden code forces the Approval Gate ([#31](https://github.com/BasantPandey/CmdWarden/issues/31)). Other wrappers stay a residual (PATH-only) |
| Approval clickjacking, self-approval by injected input | Native UI; Approve accepts only non-injected hook input, no UI Automation Invoke ([#23](https://github.com/BasantPandey/CmdWarden/issues/23)); phone OOB out of scope |

Broader Automic comparison: automic-vault-architecture.md.

---

## 16. Explicit non-goals (v1 / this handoff)

- 100+ tool harden catalog
- Phone / out-of-band approval
- API hooking / detours
- CLI flag parity with macOS Automic Vault
- Session 0 Windows Service as agent host
- Upstream multi-OS monorepo with automic-vault
- Enterprise MDM / multi-user org as first-class driver
- Polished tray Settings app
- Windows **bless** / capability-bundled scripts (future note only)
- Timed session grants ("allow write for 10 minutes") and a live management strip in the shell; persisting approval memory across agent restarts (approval memory v1, [#130](https://github.com/BasantPandey/CmdWarden/issues/130))
- Downloadable scan rule packs; continuous scan watcher
- Machine-wide install, winget/MSIX as primary, auto-update, nuget.org requirement
- Strong-mode store strip as default for git/az/docker

---

## 17. Implementation status and next build work

### Done

- Spike foundation: bootstrap, Session Agent health, vault save/inject, hybrid identity, policy gate, Approval Gate (#1 to #6).
- Spike vertical: gh shim, `cw harden gh`, gh classifier, audit trail (#7 to #10).
- First-catalog compat harden for `git` / `az` / `docker`, scan detectors, dotnet tool packaging, management shell tabs.
- Approval memory ([#129](https://github.com/BasantPandey/CmdWarden/issues/129)) and the shipped steps of the Windows gaps plan.
- First-catalog strong mode, every slice (first-catalog-strong-mode-plan.md section 4).

### Next build work

The open steps of automic-windows-gaps-plan.md section 4. Open bug: [#211](https://github.com/BasantPandey/CmdWarden/issues/211).

### Decision index (wayfinder)

See map [CmdWarden product handoff (beyond spike)](https://github.com/BasantPandey/CmdWarden/issues/12) **Decisions so far** for one-line gists and links to resolution comments (#13–#25).

---

## 18. Related documents

| Doc | Role |
|-----|------|
| [the Glossary](../glossary.md) | Ubiquitous language |
| [docs/spec/approval-gate-ui.md](./approval-gate-ui.md) | Approval Gate WinUI card implement handoff |
| [docs/spec/vault-secrets-ui.md](./vault-secrets-ui.md) | Vault secrets manager (Secrets tab) implement handoff |
| [docs/spec/management-shell-tabs.md](./management-shell-tabs.md) | Management shell tabs implement plan (Doctor, Hardened Tools, Secret Usage, Detectors, Secret Gates) |
| [Home](../index.md) | Build and run |
| [docs/user-guide.md](../user-guide.md) | End-user guide |
| Git history before `14e5e6b` | Research notes, prototypes, and the implement plans |
| GitHub issues | Spike implement + closed decision tickets |

---

*End of handoff specification.*
