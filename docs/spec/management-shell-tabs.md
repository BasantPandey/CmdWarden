# Management Shell Tabs - Implement Plan

**Status:** Handoff-ready (wayfinder map [#103](https://github.com/BasantPandey/CmdWarden/issues/103))  
**Product:** CmdWarden  
**Scope:** Fill the five stub tabs of the CmdWarden Vault management shell as read-only dashboards: Doctor, Hardened Tools, Secret Usage, Detectors, Secret Gates  
**Stack:** WPF (`net10.0-windows`), unpackaged Start-menu app, existing hybrid shell from PR #102  
**Domain language:** [Glossary](../glossary.md)  
**Architecture handoff:** [cmdwarden.md](./cmdwarden.md)  
**PRDs:** [#112](https://github.com/BasantPandey/CmdWarden/issues/112) (Doctor, Hardened Tools, Secret Usage, Detectors), [#113](https://github.com/BasantPandey/CmdWarden/issues/113) (Secret Gates)

This plan consolidates the closed map decisions so implement sessions can ship one tab at a time without reopening product questions. It contains no production code.

---

## 1. Goals and non-goals

### Goals

- Every tab shows the same facts the CLI prints, sourced from `CmdWarden.Contracts`, never by scraping CLI output.
- Every tab is **read-only**. Where the CLI has an action, the tab shows a one-line hint naming the CLI command.
- Secret **values** never appear on any surface.
- The shipped Secrets tab and every CLI command keep their current behavior.

### Non-goals (v1)

| Out | Why |
|-----|-----|
| Any mutation from a tab (start agent, install shortcut, harden, edit policy, clear audit) | Read-only posture locked at chart |
| New Session Agent RPCs or proto changes | Every fact is reachable from Contracts today |
| Spawning `cw` or parsing its stdout | Rejected at [#104](https://github.com/BasantPandey/CmdWarden/issues/104) |
| Timer polling, file watchers, live tail | Load-on-open plus Refresh is enough |
| Secret Gates level editor | Out of scope on the map. Added later in [#44](https://github.com/BasantPandey/CmdWarden/issues/44): enroll, set level, unenroll |
| Automic 100+ detector catalog, per-command Allow/Ask/Block | Not product truth |
| View-model layer or automated UI tests | Shell pages are thin bindings; manual smoke like Approval Gate and Vault |

---

## 2. Shared conventions (all tabs)

These settle the map's "shared shell polish" fog. Every page follows them.

| Concern | Rule |
|---------|------|
| Load | Gather facts on tab open and on the primary button. No timer. |
| Primary button | Refresh on every tab except Detectors, which uses Run scan. Stub dialog removed. |
| Progress | Gather off the UI thread. Primary disabled and a "Checking..." or "Scanning..." state shown meanwhile. |
| Agent dependency | Only Doctor opens the pipe. The other four tabs work with the agent down and show no agent banner. |
| Hint lines | Where the CLI has an action, a muted line reads "Run `cw <verb>`". Never a button. |
| Pills | Reuse the shell's existing styles: green allow, yellow ask, red block, grey muted, plus one blue Info style. |
| Empty states | Headline plus one muted sentence. Never read as broken. |
| Errors | Red banner with the exception message. Never render raw file text. |
| Threading | Results marshalled back to the UI thread; no UI updates from worker threads. |
| Window title | Stays **CmdWarden Vault**. Revisit only if a later effort renames the product surface. |

---

## 3. Fill order and PR slices

Fill order locked at chart: **Doctor -> Hardened Tools -> Secret Usage -> Detectors -> Secret Gates**.

One PR per tab. Each PR carries its own Contracts piece and tests, so every PR lands green and demoable alone. Doctor establishes the shared page pattern; the other four reuse it and can run in parallel after Doctor.

| PR | Contracts piece | Shell page | Blocked by |
|----|-----------------|------------|------------|
| 1 | Health client moved from CLI (behavior-preserving) | Doctor | none |
| 2 | Hardened tool status probe + fixed catalog | Hardened Tools | 1 |
| 3 | Audit record line parser | Secret Usage | 1 |
| 4 | Scan engine moved from CLI (pure move) | Detectors | 1 |
| 5 | Policy read model over the policy store | Secret Gates | 1 |

---

## 4. Seam and testing

**One seam: the `CmdWarden.Contracts` surface.** All probing, parsing, scanning, and policy reading lives there. Shell pages bind to it and contain no logic of their own.

| Piece | Test shape | Prior art |
|-------|------------|-----------|
| Health client | Existing agent-process tests keep passing after the move | `SessionAgentHealthTests`, `AgentLifecycleTests` |
| Hardened tool status probe | Temp product root, injected PATH; Hardened, three Degraded cases, Not hardened, unknown id ignored | `ToolPinStoreTests`, harden tests |
| Audit record parser | Round-trip, malformed JSON -> null, missing fields -> null, extra fields tolerated | `AuditLogTests` |
| Scan engine | Existing `ScanTests` move unchanged; add one ordering test | `ScanTests` |
| Policy read model | Temp policy file; defaults, overrides, empty, malformed surfaces as error not defaults | `PolicyStoreTests` |

CLI regression: `cw doctor`, `cw audit`, `cw scan`, `cw policy list` tests pass unchanged after the moves.

Secret hygiene: no fixture writes a value into a pin, audit line, finding, or policy file.

---

## 5. Tab briefs

Each brief gists the closed ticket. The ticket's resolution comment is the source of record.

### 5.1 Doctor - [#105](https://github.com/BasantPandey/CmdWarden/issues/105)

Research: doctor-ui-data-sources.md ([#104](https://github.com/BasantPandey/CmdWarden/issues/104)).

- **Cards:** Session Agent (Healthy / Down, pipe), Vault UI binary (Found / Not found, path), Start Menu shortcut (Present / Missing, path).
- **Details:** always-visible two-column list. Always: product name and version, product root, expected pipe, agent binary path. When up: agent version, pid, user, machine, caller pid, launcher kind, launcher policy key, auto-approve eligible. Agent-only rows show "-" when down.
- **Data:** lifecycle status call for up/down; one `GetHealth` call when up via the health client moved into Contracts. Local locators for everything else. Never the lazy-start path.
- **Version mismatch:** yellow pill on the agent card when app and agent versions differ; both shown in Details. Warning, not error.
- **Hints:** Down -> "Run `cw doctor` in a terminal to start it." Missing shortcut -> "Run `cw shortcut install`." No hint for a missing binary.
- **Non-goals:** start/stop, shortcut install, `cw doctor` spawn, process chain, secret count.

### 5.2 Hardened Tools - [#106](https://github.com/BasantPandey/CmdWarden/issues/106)

- **Cards:** fixed four in catalog order: GitHub CLI, Git, Azure CLI, Docker CLI. Unknown pin ids ignored.
- **Status from three facts:** pin check (ok / missing / mismatch), shim exe under the shims dir, shims dir in the user PATH.
  - **Hardened** (green): all three good.
  - **Degraded** (yellow): pin mismatch, pinned path gone, or shim present but shims dir not on PATH. Reason line shown.
  - **Not hardened** (grey): no pin and no shim. Hint "Run `cw harden <tool>`".
- **Card:** name, muted tool id, pinned real-tool path or "-", pill, reason or hint. Nothing from policy or audit.
- **Refresh:** hash recomputed every time, off the UI thread. No cache.
- **Accepted gap:** a tool that is not installed shows Not hardened; discovery stays in the CLI.

### 5.3 Secret Usage - [#107](https://github.com/BasantPandey/CmdWarden/issues/107)

- **Data:** audit NDJSON files read directly through the Contracts audit log reader, newest 200 lines, each parsed to a typed gate record by the new parser.
- **Row:** local time ("10:04", "Yesterday 09:51", else short date and time); launcher policy key with kind as muted suffix when different; tool and command class ("gh · read"); secret name or "-"; decision pill (green auto-allow, yellow approved, red denied) with reason code muted.
- **Footer:** "Showing newest 200 of the 30-day trail. Run `cw audit -n <count>` for more."
- **States:** empty -> "No secret usage recorded yet." plus note that gate decisions appear after a hardened tool runs and values are never shown. Unparseable lines skipped and counted ("N entries could not be read"). Unreadable directory -> red banner.
- **Non-goals:** filters, search, paging, export, row expand, live tail, vault save/delete events, last-used rollups, clear or prune.

### 5.4 Detectors - [#108](https://github.com/BasantPandey/CmdWarden/issues/108)

- **Engine:** scan engine, context, finding, severity, detector interface, and the eleven detectors move from the CLI into a Scan namespace in Contracts, unchanged. Shell runs it in-process.
- **Findings only:** one card per finding. No detector catalog, no enable or disable, no Add Detector. Primary is Run scan.
- **Card:** title, muted tool id, severity pill (red High, yellow Medium, grey Low, blue Info), summary, evidence as muted monospace, remediation line, harden hint as a hint line. No buttons.
- **Order:** severity desc, then catalog tool order, then id. Scope Info card last as a footer.
- **Environment truth:** the app scans its own login environment. The shell appends to the scope card: "Scanned from the app's environment. Run `cw scan` in a terminal to check that session's variables."
- **Clean state:** green "No findings" headline with the scope card beneath. Header shows "Last scan: HH:mm". No persistence.

### 5.5 Secret Gates - [#109](https://github.com/BasantPandey/CmdWarden/issues/109)

- **Model:** product truth. Four levels Deny / Read / Trusted / Full, each rendered with the class matrix from [cmdwarden.md section 6](./cmdwarden.md) as explanatory text. No per-command Allow / Ask / Block.
- **Defaults card first:** "AI Harness -> Read", "Terminal -> Trusted", with matrix and a hint naming the CLI verb.
- **One card per enrolled launcher:** header is policy key, kind pill (blue AI Harness / grey Terminal), display path muted and left-truncated. Rows are the four catalog tools with level pill and a muted "(kind default)" marker when not overridden. Level colors: Deny red, Read blue, Trusted yellow, Full green.
- **Read-only:** hints name `cw policy set` and `cw policy enroll --kind terminal|ai-harness`. Primary is Refresh. No edit or delete icons.
- **Data:** policy store read from the same path `cw policy path` reports. No agent RPC.
- **States:** empty -> Defaults card, then "No launchers enrolled. Every tool resolves to Deny until a launcher is enrolled." with enroll hint. Malformed file -> red banner with message and path, cards hidden, no silent fallback to defaults.
- **Out of scope:** level editor, usage data on cards, identity detail (hash, thumbprint).

---

## 6. Decision index (wayfinder)

Map [Management shell tabs: fill stubs (Doctor -> Gates)](https://github.com/BasantPandey/CmdWarden/issues/103), **Decisions so far**, links each resolution comment: [#104](https://github.com/BasantPandey/CmdWarden/issues/104), [#105](https://github.com/BasantPandey/CmdWarden/issues/105), [#106](https://github.com/BasantPandey/CmdWarden/issues/106), [#107](https://github.com/BasantPandey/CmdWarden/issues/107), [#108](https://github.com/BasantPandey/CmdWarden/issues/108), [#109](https://github.com/BasantPandey/CmdWarden/issues/109).

## 7. Related documents

| Doc | Role |
|-----|------|
| [cmdwarden.md](./cmdwarden.md) | Architecture handoff |
| [vault-secrets-ui.md](./vault-secrets-ui.md) | Secrets tab brief (shipped) |
| Git history before `14e5e6b` | Shell prototype and Doctor data source notes |
| [Glossary](../glossary.md) | Ubiquitous language |

---

*End of implement plan.*
