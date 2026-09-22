# Approval Gate UI - Implement Handoff

**Status:** Handoff-ready (wayfinder map [#68](https://github.com/BasantPandey/CmdWarden/issues/68))  
**Product:** CmdWarden  
**Scope:** User-facing Approval Gate chrome only - not a full management app  
**Stack target:** WinUI 3 / Windows App SDK, unpackaged helper process  
**Domain language:** [Glossary](../glossary.md)  
**Architecture handoff:** [cmdwarden.md](./cmdwarden.md) (points here for UI)

This brief consolidates closed map decisions so implementers can ship the Automic-style **Deny / Approve Once** card without reopening tickets. It does **not** contain production WinUI code.

---

## 1. Goals and non-goals

### Goals

- Replace the spike **MessageBox** Approval Gate with a polished **card** that does the same *job* as Automic Vault's approval prompt, with **Windows-native** chrome (not a macOS pixel twin).
- Keep **synchronous** `IApprovalGate.Prompt` semantics: Authorize / secret release **blocks** until the user decides or the gate fails closed.
- Show only **secret names**, never secret **values**.
- Ship as an **out-of-process** unpackaged WinUI helper invoked by the Session Agent.

### Non-goals (v1 / this handoff)

| Out | Why |
|-----|-----|
| Full management app (Secret Gates list, Detectors UI, Secret Usage browser, Doctor window) | Map #68 out of scope |
| **Always Approve** (or other durable grant) on the dialog | Durable policy stays CLI (`cw policy enroll` / `set`) |
| **Windows Hello** / OS step-up | Deferred; [issue #20](https://github.com/BasantPandey/CmdWarden/issues/20) |
| Phone / out-of-band approval | Product non-goal |
| Marketing site redesign | Separate effort |
| In-process WinUI inside `CmdWarden.Agent` | Research #69: not recommended |

---

## 2. Relation to issue #20 and MessageBox

| Topic | Decision |
|-------|----------|
| Windows Hello / step-up | **Still out of v1** ([Grilling: Windows Hello / step-up in v1 policy](https://github.com/BasantPandey/CmdWarden/issues/20)) |
| MessageBox spike | **Chrome superseded** by this WinUI card design. MessageBox may remain a **fallback** or CI-adjacent path until the helper ships (`CW_APPROVAL_MODE`) |
| Fail closed | Unchanged: UI unavailable → `Unavailable` → block grant |
| Outcomes | Domain enum unchanged: `AllowOnce`, `Deny`, `Unavailable` |

---

## 3. Surface layout

**Accepted prototype (visual source of truth):**

- docs/prototypes/approval-gate-card.html - interactive mock  
- docs/prototypes/approval-gate-card.md - structure notes  

### Top → bottom (main surface)

1. Native Windows title bar: Lintel product mark + **`CmdWarden`** (see [Window chrome](#window-chrome))
2. In-window compact brand row: 20px Lintel mark, 8px gap, 12px secondary **`CmdWarden`**
3. Large **launcher icon** (shell/exe; generic fallback)
4. Launcher **display name** + subtitle **`wants to run`**
5. **Command block** (dark): tool invocation; resolved tool path; meta row **`cwd`** / **`keys`** (names only)
6. Soft reason **heading** (e.g. `GitHub token requested`) + locked **reason line**
7. **Details** expander (**collapsed** by default)
8. Buttons: **Deny** · **Allow for session** (enrolled launchers only) · **Approve Once** (Approve Once is primary)

No durable-policy footnote. No enrollment badge or publisher on the main surface.

### Window chrome

Locked on [Approval Gate title bar and header chrome](https://github.com/BasantPandey/CmdWarden/issues/138). Do not reopen look-and-feel.

| Surface | Rule |
|---------|------|
| OS title bar | **Native** Windows caption. No custom `WindowChrome`. No hidden chrome. |
| Caption icon | **A - Lintel** product mark. Unpackaged WPF needs a multi-size `.ico` via `ApplicationIcon` (16, 24, 32, 48, 256). PNG/XAML cannot stamp the native caption. Research: docs/research/wpf-native-title-bar-icon.md. |
| Window title | `CmdWarden` (one word) |
| In-window header | **A - Compact row**: 20px Lintel, 8px gap, 12px semibold secondary `CmdWarden`. No hairline. No brand band. Prototype: docs/prototypes/approval-gate-header.html. |
| Mark geometry | Cyan rounded-square tile (`#60CDFF` → `#0078D4`), two posts + flat top bar, off-white, no letters. Prototype: docs/prototypes/cmdwarden-product-mark-lintel.svg. |

The large hero icon in the card body stays **launcher** identity, not the product mark.

---

## 4. Microcopy

From [Grilling: Approval Gate microcopy and labels](https://github.com/BasantPandey/CmdWarden/issues/71):

| Surface | Copy |
|--------|------|
| Window / dialog title | `CmdWarden` |
| Launcher display name | Resolved friendly name when available |
| Unknown / unenrolled launcher | `Unknown app` |
| Subtitle under name | `wants to run` (sentence case) |
| Reason (purpose present) | `{Tool} needs {secret name} for {purpose-or-account}` |
| Reason (purpose missing) | `{Tool} needs {secret name}` |
| Primary buttons | **Deny** · **Allow for session** (enrolled only) · **Approve Once** |
| Durable-policy footnote | **None** |

**Convention:** User-facing **Approve Once** maps to domain **`ApprovalOutcome.AllowOnce`** / "Allow once" in architecture text. No enum rename required.

**Never show:** secret **values**.

---

## 5. Launcher identity on the main surface

From [Grilling: launcher identity on the gate surface](https://github.com/BasantPandey/CmdWarden/issues/72):

| Element | Rule |
|--------|------|
| Large name priority | PE version **ProductName** → **FileDescription** → **file name** (with extension if that is the fallback) → **`Unknown app`** |
| Path as title | **Never** |
| Icon | Shell/exe icon from launcher path when possible; else **generic** placeholder |
| Enrollment on main | **None** (not a trust seal) |
| Publisher / signature on main | **None** (Details only) |

### Never on the main surface

- Full process chain dump  
- Raw policy keys (`auth:sha1:…`, `pathhash:sha256:…`)  
- Secret values  
- Full cert thumbprints as hero text  

---

## 6. Details expander

From [Grilling: Details expander contents](https://github.com/BasantPandey/CmdWarden/issues/73):

- **Collapsed by default**
- Optional - never required to act

### Fields (recommended order)

| Field | Notes |
|--------|--------|
| Enrollment kind | Terminal / AI harness / not enrolled (UI wording) |
| Identity kind | authenticode / pathhash / unknown |
| Publisher | Authenticode simple name, or "Unsigned" |
| Launcher path | Full path when known |
| Policy level | Deny / Read / Trusted / Full |
| Command class | read / write / secret-reveal / unknown |
| Policy key | **Full** lookup string |
| Request timestamp | Local time of prompt |

### Non-goals for Details

Secret values, full process chain, tool pin path, full argv dump beyond the main command block, MessageBox-style instruction lines.

---

## 7. Actions and outcomes

| UI control | `ApprovalOutcome` | Effect |
|------------|-------------------|--------|
| **Approve Once** | `AllowOnce` | Launcher process keeps this one class for tool + secret until exit or 60 idle minutes; never secret-reveal; no lasting policy change (#205) |
| **Allow for session** | `AllowForSession` | Launcher process keeps this class (and lower) for tool + secret until exit or 60 idle minutes; never secret-reveal; hidden for unenrolled launchers (#132) |
| **Deny** | `Deny` | Block grant |
| Cannot show UI / timeout / helper crash / bootstrap fail | `Unavailable` | **Fail closed** (block) |
| `CW_APPROVAL_MODE=allow` | `AllowOnce` | CI / scripted |
| `CW_APPROVAL_MODE=deny` | `Deny` | CI / scripted |
| `CW_APPROVAL_MODE=off` | `Unavailable` | Fail closed |

Authorize and `ReleaseSecret` paths remain blocked until `Prompt` returns.

---

## 8. Fail-closed rules

| Condition | Result |
|-----------|--------|
| Non-interactive session / no desktop | `Unavailable` |
| Helper missing, will not start, or bootstrap fails | `Unavailable` |
| User does not answer within timeout | `Unavailable` (spike MessageBox used **5 minutes**; keep unless implement chooses otherwise - see open choices) |
| Audit write fails (existing vault rules) | No secret release (existing product rule) |

There is **no** silent allow if the polished UI cannot show. Map chart lock: **not** MessageBox fallback as the product fail path for "UI unavailable" - fail closed. MessageBox may still exist as an **explicit** mode for transition/CI, not as automatic soft-fail for missing App Runtime if product policy is fail closed (implement should prefer Unavailable when WinUI path is selected and cannot run).

---

## 9. Hosting architecture

From [Research: WinUI 3 hosting from Session Agent](https://github.com/BasantPandey/CmdWarden/issues/69) - artifact docs/research/winui-approval-gate-hosting.md:

```text
Authorize / ReleaseSecret (gRPC thread)
        │
        ▼
IApprovalGate.Prompt(ApprovalRequest)   // blocks; no secret values
        │
        ▼
spawn agent/approval-gate/CmdWarden.ApprovalGate.exe
  payload file: JSON (secret names only)
        │
        ▼
helper: STA WPF Fluent card → user clicks
        │
        ▼
exit code → AllowOnce (0) | Deny (1) | Unavailable (2)
        │
        ▼
agent continues grant or PermissionDenied
```

| Item | Guidance |
|------|----------|
| Process | **Out-of-process** helper - do **not** load UI into `CmdWarden.Agent` |
| UI stack | Fluent-style **WPF** (`net10.0-windows`); WinUI 3 deferred when App SDK tooling is available |
| Package path | `agent/approval-gate/` next to Session Agent (dotnet tool + zip) |
| Agent adapter | `ProcessApprovalGate` + `CW_APPROVAL_MODE=winui` |
| Isolation | Helper crash must not take down Session Agent |
| Redistributable | Helper is **.NET FDD WPF** - no Windows App Runtime; needs .NET desktop like the rest of CmdWarden |

---

## 10. Redistributable and install constraints

From [Research: Windows App SDK redistributable vs current install](https://github.com/BasantPandey/CmdWarden/issues/70) - artifact docs/research/windows-app-sdk-redistributable.md:

| Fact | Implication |
|------|-------------|
| Framework-dependent WinUI needs **Windows App Runtime** installer (~100MB+ class for x64) + often VC++ redist | Breaks pure "tiny binaries only" unless install docs chain the runtime |
| **Self-contained** (`WindowsAppSDKSelfContained=true`) avoids separate runtime install | Much larger helper payload; RID-specific packs |
| OS floor | Windows 10 1809+ (already fine for CmdWarden) |
| Unpackaged helper | Viable for per-user security helper |

**Product packaging choice (FDD vs self-contained) is not locked** - implement PRD must pick one; see open choices.

---

## 11. `ApprovalRequest` / payload gaps

Today ([ApprovalOutcome.cs](https://github.com/BasantPandey/CmdWarden/blob/main/src/CmdWarden.Contracts/ApprovalOutcome.cs)):

```text
Tool, CommandClass, PolicyLevel, LauncherPolicyKey, LauncherKind,
LauncherPath, SecretName, Purpose, EnrollmentKind, PolicyNote
```

### Required for the accepted UI (extend or resolve in helper)

| Need | Notes |
|------|--------|
| Launcher display name inputs | ProductName / FileDescription / file name - resolve from path in helper or pass precomputed |
| Launcher icon | Extract from path in helper; generic fallback |
| Tool command line / invocation | Not only tool id - full argv summary for command block |
| Resolved tool path | Real binary after pin |
| Working directory | `cwd` row |
| Secret name list | Support multiple keys later; v1 may be single `SecretName` |
| Soft reason heading | Derived copy (e.g. from tool + secret) or optional field |
| Request timestamp | Generate at prompt time if not passed |
| Publisher / identity kind | From process node / image identity (agent already knows much of this) |

**Never** pass secret **values** on argv, env, or IPC payload to the helper.

---

## 12. CI and factory modes

Preserve `CW_APPROVAL_MODE`:

| Mode | Behavior |
|------|----------|
| default / `prompt` / `ui` / `winui` | Process helper card; missing helper → Unavailable |
| `messagebox` / `native` | Transitional MessageBox |
| `allow` | Auto `AllowOnce` |
| `deny` | Auto `Deny` |
| `off` | `Unavailable` |
| optional `winui` | Force helper if multi-mode transition needed |

Tests must not require an interactive desktop; scripted modes remain mandatory.

---

## 13. Open implement choices (still fog / product discretion)

These are **in scope for implement**, not reopened design tickets unless blocked:

| Topic | Notes |
|-------|--------|
| Theme | Prototype used light card + dark command block; system Fluent light/dark vs fixed not locked |
| Concurrent prompts | One-shot helper per prompt; queueing / multi-monitor / focus-stealing TBD |
| Timeout duration | Spike: 5 minutes; confirm for WinUI helper |
| Accessibility bar | Beyond keyboard defaults / focus order TBD |
| FDD vs self-contained shipping | Facts in research #70; product pick required |
| MessageBox cutover | How long dual-path remains |
| Exact helper IPC contract | Exit codes vs JSON stdout vs short-lived pipe |

---

## 14. Decision index (map #68)

| Ticket | Gist |
|--------|------|
| [#69](https://github.com/BasantPandey/CmdWarden/issues/69) | Out-of-process unpackaged WinUI helper |
| [#70](https://github.com/BasantPandey/CmdWarden/issues/70) | Runtime installer vs self-contained size tradeoffs |
| [#71](https://github.com/BasantPandey/CmdWarden/issues/71) | Microcopy table |
| [#72](https://github.com/BasantPandey/CmdWarden/issues/72) | Name priority, icon, main-surface bans |
| [#73](https://github.com/BasantPandey/CmdWarden/issues/73) | Details forensic-lite, collapsed |
| [#74](https://github.com/BasantPandey/CmdWarden/issues/74) | Layout prototype accepted |
| [#75](https://github.com/BasantPandey/CmdWarden/issues/75) | This brief + `cmdwarden.md` pointer |
| [#20](https://github.com/BasantPandey/CmdWarden/issues/20) | Hello out of v1 |
| [#138](https://github.com/BasantPandey/CmdWarden/issues/138) | Native title bar Lintel icon + compact in-window header |

Map: [Approval Gate UI: Automic-style card (not MessageBox)](https://github.com/BasantPandey/CmdWarden/issues/68). Chrome pass: [Approval Gate title bar and header chrome](https://github.com/BasantPandey/CmdWarden/issues/138).

---

*End of Approval Gate UI handoff.*
