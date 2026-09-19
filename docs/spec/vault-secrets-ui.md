# Vault Secrets UI - Implement Handoff

**Status:** Handoff-ready (wayfinder map [#83](https://github.com/BasantPandey/CmdWarden/issues/83))  
**Product:** CmdWarden  
**Scope:** Vault secrets management UI: list names, add/save, delete -- never show secret values  
**Stack target:** WinUI 3 / Windows App SDK, unpackaged Start-menu app  
**Domain language:** [Glossary](../glossary.md)  
**Architecture handoff:** [cmdwarden.md](./cmdwarden.md) (points here for vault UI)

This brief consolidates closed map decisions so implementers can ship the vault secrets manager without reopening tickets. It does **not** contain production WinUI code.

---

## 1. Goals and non-goals

### Goals

- Provide a **Start-menu** WinUI 3 window where the user can **list secret names**, **add/save**, and **delete** vault entries.
- Show only **secret names** -- never secret **values** on any surface.
- Operate as a **Session Agent gRPC client** -- agent-only data path; no direct CredMan access from UI.
- Ship as a **separate unpackaged** process (not in-process with the agent, not shared with Approval Gate).

### Non-goals (v1 / this handoff)

| Out | Why |
|-----|-----|
| Full Automic-style management app (Secret Gates, Detectors, Hardened Tools browser, Secret Usage analytics) | Map #83 out of scope |
| Always-on tray shell / polished Settings app | Product non-goal |
| Reveal or export of secret **values** in the UI | Standing charting decision |
| Windows Hello / step-up for vault access | Deferred |
| Replacing or removing CLI vault commands (`cw save` / `delete` / `inject`) | CLI remains for power users |
| Policy editor / enrollment UI / Approval Gate redesign | Owned by other maps |
| Inject / release / reveal of secrets from the manager UI | Inventory only |

---

## 2. Surface layout

**Prototype:** [#88](https://github.com/BasantPandey/CmdWarden/issues/88) -- **Variant B (Modern Fluent cards)** accepted as the visual source of truth: `docs/prototypes/vault-secrets-manager.html`.

### Top to bottom (main surface)

1. Window title bar: **`CmdWarden Vault`**
2. **Toolbar**: **Add secret** | **Refresh** (left); secret count e.g. "4 secrets" (right). No toolbar Delete button.
3. **Secret name list**: rounded/bordered card rows, name-only (no last-updated, no value size). Inline **Delete** button per card.
4. **Agent-down banner** (persistent when agent unreachable): list/add/delete **disabled**
5. **Empty vault** (agent up, zero secrets): headline **No secrets yet**; body note that values are never shown; primary **Add secret** button

No master-detail split. No CLI command line on the surface. No secret values anywhere.

---

## 3. Microcopy

From [Grilling: vault manager chrome and empty states](https://github.com/BasantPandey/CmdWarden/issues/86):

| Surface | Copy |
|--------|------|
| Window title / Start Menu entry | `CmdWarden Vault` |
| Toolbar buttons | **Add** · **Refresh** · **Delete** |
| Empty vault headline | `No secrets yet` |
| Agent-down banner | `Session Agent not running` (or unreachable); actions disabled |
| Delete disabled (no selection) | Delete button disabled when no row selected |

**Never show:** secret **values**.

---

## 4. Interaction rules

From [Grilling: add and delete interaction rules](https://github.com/BasantPandey/CmdWarden/issues/87):

### Add / save

| Rule | Detail |
|------|--------|
| Surface | **Modal dialog**: Name field + masked Value field; **Cancel** / **Save** |
| Validation | Name: required; letters, digits, `_`, `-`, `.` (leading `+` stripped same as `VaultNames`). Value: non-empty, within CredMan **2560** byte limit. Inline field errors or disable Save until valid |
| Overwrite | If name already exists, show **confirm replace** dialog (name only; value never shown). On confirm, call `SaveSecret` (same overwrite as CLI) |
| Success | Refresh list after save |

### Delete

| Rule | Detail |
|------|--------|
| Affordance | Inline **Delete** button on each card (Variant B chrome) -- not a toolbar button |
| Selection | Card must be **selected** (click to select) before its Delete button is enabled/actionable; hover alone does not arm it |
| Confirm | **Confirm by name** dialog: irreversible; value not shown. **Cancel** / **Delete** |
| Success | Refresh list after delete |

### Refresh

- Re-fetches name list from agent via `ListSecretNames`.
- No polling; manual or on window activation (implement choice).

### Inject / release / reveal

- **Not in this UI.** Manager is inventory only: list / add / delete. Use stays shim, `cw inject`, Approval Gate.

---

## 5. Agent contract

Today the Session Agent gRPC service has `SaveSecret`, `ReleaseSecret`, and `DeleteSecret`. A **new `ListSecretNames` RPC** is required for this UI. The Approval Gate is **not** triggered for list (inventory, not release).

### ListSecretNames (new -- needs proto + impl)

```
rpc ListSecretNames (ListSecretNamesRequest) returns (ListSecretNamesResponse);
```

- Backed by `CredEnumerateW` with filter `CmdWarden/secret/*`, flags `0`.
- Agent strips the `VaultNames.TargetPrefix`; returns logical names only.
- Never returns secret **blobs** on the wire.
- Empty vault → success with zero names (not an error).
- `ERROR_NO_SUCH_LOGON_SESSION` → agent failure; UI shows degraded banner.

Research: list-vault-secret-names (ticket [#84](https://github.com/BasantPandey/CmdWarden/issues/84)).

### Existing RPCs reused as-is

| RPC | UI use | Notes |
|-----|--------|-------|
| `SaveSecret` | Add (and overwrite) | Same as CLI; UI sends name + value bytes |
| `DeleteSecret` | Delete | Same as CLI; after confirm dialog |
| `GetHealth` | Health check / agent detection | Used for banner state and reconnect |

### Validation shared with CLI

- `VaultNames.TargetName()` rules (letters, digits, `_`, `-`, `.`; leading `+` strip).
- CredMan 2560-byte blob limit.
- No additional product-only validation rules.

### Audit

- **Deferred.** UI uses same save/delete RPCs as CLI; no new save/delete audit requirement in this handoff. Gate audit (NDJSON) unchanged.

---

## 6. Hosting architecture

From [Research: WinUI Start-menu secrets manager hosting](https://github.com/BasantPandey/CmdWarden/issues/85) -- artifact docs/research/winui-secrets-manager-hosting.md:

```text
Start Menu .lnk  (per-user Programs folder)
        │
        ▼
CmdWarden.SecretsManager.exe   // unpackaged WinUI 3, single-instance
  Bootstrap App SDK → MainWindow (list / add / delete names only)
        │
        │  gRPC over named pipe (same AgentEndpoints as CLI)
        ▼
CmdWarden.Agent (Session Agent)  // separate process; vault + policy
        │
        ▼
Windows Credential Manager (CRED_TYPE_GENERIC)
```

| Item | Guidance |
|------|----------|
| Process | **Separate process** from Session Agent and Approval Gate helper |
| UI stack | **WinUI 3** unpackaged (`WindowsPackageType=None`) |
| Package path | `secrets-manager/` at tool/zip root (or `agent/secrets-manager/`) |
| IPC | gRPC over named pipe (`AgentEndpoints.PipeName`); reuse `AgentChannelFactory` |
| Single-instance | Via Windows App SDK `AppInstance.FindOrRegisterForKey` + redirect on re-launch |
| Lifetime | **Open on demand**; process exits on close; no required tray |
| Reconnect | Recreate gRPC channel on failure; optionally call `AgentLifecycle.EnsureRunningAsync` |
| Agent-down | UI stays open with banner; no direct CredMan fallback |

Contrast with Approval Gate helper (WPF, one-shot, spawn/exit-code, no Start menu) -- separate concerns, separate processes.

---

## 7. Fail modes

| Condition | Behavior |
|-----------|----------|
| Agent not running / unreachable | Persistent banner; list/add/delete **disabled**; optional "Start agent" secondary action |
| Agent not installed / binary missing | Degraded error; point to install docs / `cw doctor` |
| Pipe ACL / wrong user session | Same as unreachable banner |
| Agent version skew | Handle via health/version fields when present |
| Windows App Runtime missing (FDD) | Bootstrap failure before UI; install remediation / runtime installer |
| Non-interactive session / no desktop | WinUI requires interactive session; not in scope for remote/Session-0 |
| Elevated manager | Keep medium IL like agent/CLI |
| ListSecretNames returns empty | Normal (empty vault); show "No secrets yet" |

None should crash the shell; all should be recoverable without rebooting.

---

## 8. Install and shortcut registration

| Item | Guidance |
|------|----------|
| Shortcut | Per-user **`.lnk`** under `%APPDATA%\Microsoft\Windows\Start Menu\Programs\` pointing at manager EXE |
| Install hook | `scripts/Install-CmdWarden.ps1` or post-tool-install docs create/update shortcut |
| Update | Recreate shortcut on tool update (dotnet tool store path changes across versions) |
| Uninstall | Delete `.lnk` on `dotnet tool uninstall` / zip cleanup |
| Env override | `CW_SECRETS_MANAGER_PATH` (pattern like `CW_AGENT_PATH`) |
| `cw doctor` | Optional: report shortcut present / manager binary found / WASDK runtime OK |

No App Execution Alias, no sparse package -- pure unpackaged Win32 shortcut.

---

## 9. Open implement choices

These are **in scope for implement**, not reopened design tickets unless blocked:

| Topic | Notes |
|-------|-------|
| Theme | System Fluent light/dark vs fixed not locked |
| List-names proto fields | Exact message shape (name only vs name + optional metadata) |
| Concurrent CLI edits while manager open | Refresh on focus or polling interval |
| Refresh policy | Manual only (Refresh button) vs auto-refresh on window activation |
| Accessibility bar | Beyond keyboard defaults / focus order TBD |
| FDD vs self-contained shipping | Facts in windows-app-sdk-redistributable.md; product pick required |
| List density | Visual density beyond name-only rows TBD |
| Empty-state illustration | Beyond text headline TBD |
| Start agent from manager | Secondary action in banner vs defer to `cw agent start` |

---

## 10. Decision index (map #83)

| Ticket | Gist |
|--------|------|
| [#84](https://github.com/BasantPandey/CmdWarden/issues/84) | `CredEnumerateW` filter `CmdWarden/secret/*`; new `ListSecretNames` gRPC; names only, no blobs |
| [#85](https://github.com/BasantPandey/CmdWarden/issues/85) | Separate WinUI 3 exe, Start Menu `.lnk`, single-instance, gRPC client, FDD shared runtime |
| [#86](https://github.com/BasantPandey/CmdWarden/issues/86) | Window title `CmdWarden Vault`; list + toolbar; empty "No secrets yet"; agent-down banner; name-only rows |
| [#87](https://github.com/BasantPandey/CmdWarden/issues/87) | Add modal (name + masked value); confirm replace on overwrite; delete confirm by name; inventory only; audit deferred |
| [#88](https://github.com/BasantPandey/CmdWarden/issues/88) | Variant B (Modern Fluent cards) accepted; delete stays inline per-card but gated by selection to honor #87 |
| [#89](https://github.com/BasantPandey/CmdWarden/issues/89) | Handoff shape: this brief (`docs/spec/vault-secrets-ui.md`), patch `cmdwarden.md` §7 |

Map: [Vault secrets UI: list / add / delete (design handoff)](https://github.com/BasantPandey/CmdWarden/issues/83).

---

*End of Vault Secrets UI handoff.*
