---
title: Test plan
description: How to test CmdWarden before a release. Automated checks first, then the manual checks that only a person can do.
---

# Test plan

Use this page before each release. Part 1 is automated. Part 2 is manual. Part 3 tells why some tests stay manual.

Write **Pass** or **Fail** in the Result column. For each Fail, open an issue with the test ID, the step, and a screenshot.

---

## Part 1 - Automated checks

Run these on your PC from the repo root. A person must be logged on to the desktop, because some tests open the real Approval Gate card.

| ID | Command | Expect | Result |
|----|---------|--------|--------|
| A1 | `dotnet test CmdWarden.sln` | All tests pass. This includes the `Process` tests that CI skips. | |
| A2 | `pwsh ./scripts/Build-Portable.ps1 -Out artifacts/portable` | The build ends with no error. | |
| A3 | `pwsh ./scripts/Test-Portable.ps1 -Folder artifacts/portable` | Each line starts with `ok`. The folder runs with no .NET on PATH. | |
| A4 | `pwsh scripts/render-ui-shots.ps1`, then look at `git diff --stat docs/images` | Only the screens that you changed are different. Open each changed image and look for cut text or bad alignment. | |

CI runs A2, A3, and A1 without the `Process` tests. The `Process` tests start real processes, named pipes, and the card. So run A1 on your PC before a release.

---

## Part 2 - Manual checks

### Before you start

- Use a clean Windows 10 or 11 user account, a VM, or Windows Sandbox. Do not use your daily account for the install and uninstall tests.
- Use test logins only: a test GitHub account, a test npm token, and so on. Never use a real production token.
- Install the release under test with the setup zip. After [#64](https://github.com/BasantPandey/CmdWarden/issues/64), also test winget.
- Have at least one AI harness: Claude Code, Cursor, or Codex. Test each one that you have.
- Look at each screen with care. Look for cut text, bad alignment, wrong colors, and blur. Test at 100% and 150% display scale, and in light and dark mode.

### 2.1 Install and set up

| ID | Steps | Expect | Result |
|----|-------|--------|--------|
| M1 | After #64: run `winget install BasantPandey.CmdWarden`. Open a new terminal. Run `cw version`. | No admin prompt. `cw version` shows the release version. | |
| M2 | On a second clean user, extract the setup zip. Double-click `install.cmd`. | No admin prompt. **CmdWarden** is in Settings > Apps. The installer offers to run `cw setup`. | |
| M3 | Run the signature check from [Install, section 4](../install.md#4-verify). | Each row shows `Valid`. | |
| M4 | Run `cw setup`. Press Enter at each question. | Each step asks first. Each step ends with a clear result. The last step shows the card of `cw try`. | |
| M5 | Run `cw setup` again. | Each step says that it is done. Nothing changes. | |
| M6 | Run `cw doctor`. | Session Agent **UP**. No red row. | |

### 2.2 Approval Gate card

These tests need a real keyboard and a real mouse. The card refuses input from scripts on purpose. See Part 3.

| ID | Steps | Expect | Result |
|----|-------|--------|--------|
| G1 | Run `cw try`. Look at the card. | The card is on top of all windows. It shows the launcher, the command, the folder, and the key name `CW_TRY_TOKEN`. It never shows a value. Text is not cut. Buttons are aligned. | |
| G2 | Run `cw try`. Press **Esc** at once, before 600 ms. | The card closes at once. The terminal shows `Blocked.` | |
| G3 | Run `cw try`. Press **Enter** at once, before 600 ms. | Nothing happens. The card stays open. | |
| G4 | Run `cw try`. Wait 1 second. Press **Enter**. | The card closes. The stand-in agent gets the fake token. `cw audit -n 5` shows an approve row. | |
| G5 | Run `cw try`. Click **Approve Once** with the mouse. | Same result as G4. | |
| G6 | Run `cw try`. Click **Deny**. | `Blocked.` and a deny row in `cw audit -n 5`. | |
| G7 | Run `cw try`. Close the card with the X or Alt+F4. | The command is blocked (fail closed). | |
| G8 | In the AI harness, ask the agent to run a `gh` write, for example `gh issue create` on a test repo. The card shows **Allow for session**. Press **1**. | The card closes. The write runs once. `cw policy sessions` shows no new grant. | |
| G9 | Do G8 again. Press **A**. | The card closes. `cw policy sessions` shows a new grant. | |
| G10 | Do G8 again. Press **Enter**. | Same result as G9. | |
| G11 | Do G8 again. Select **10 minutes**, then click **Allow for session**. | The grant in `cw policy sessions` ends 10 minutes later. | |
| G12 | Revoke the grant with `cw policy sessions --revoke <id>`. Do G8 again. | The card shows again. | |
| G13 | Deny a card. Make the same request again in 2 minutes. | No card. The request is denied at once. The tray icon tells you about the block. | |
| G14 | Run `cw policy hello secret-reveal`. In the harness, ask the agent to run `gh auth token`. Click **Approve Once**. | Windows Hello asks for your face, finger, or PIN. After you pass, the command runs. | |
| G15 | Do G14 again. Cancel Windows Hello. | The command is blocked. | |
| G16 | Run `cw try`. End the `CmdWarden.ApprovalGate` process in Task Manager while the card shows. | The command is blocked (fail closed). No secret goes out. | |

### 2.3 AI harness integration

Do these for each harness that you have: Claude Code, Cursor, and Codex. Restart the harness after `cw setup`.

| ID | Steps | Expect | Result |
|----|-------|--------|--------|
| H1 | In the harness terminal, run `cw whoami`. | The launcher is the harness, with kind **ai-harness**. | |
| H2 | Ask the agent: "Run `gh issue list` on this repo." | It runs with no card (Read class). | |
| H3 | Ask the agent: "Run `gh auth token` and show me the result." | The card shows. Deny it. The agent says that CmdWarden denied the command. | |
| H4 | Do [UC2](../use-cases/approve-secret-read-mid-session.md) part 3 with `DEMO_TOKEN`. Approve. | The command runs. The agent output shows `[CmdWarden: DEMO_TOKEN]`, not the value. | |
| H5 | Ask the agent to list its MCP tools. | The CmdWarden tools show, for example `run_with_secret`. | |
| H6 | Ask the agent to use a canary token from `cw canary status`. | An alarm shows. CmdWarden blocks the harness. | |

### 2.4 Tools and features

Each use case page has the steps and the expected result. Do the steps and compare.

| ID | Page | Result |
|----|------|--------|
| T1 | [UC1 Gate your GitHub token](../use-cases/gate-github-token-from-ai-agent.md) | |
| T2 | [UC3 Agent reads, writes need approval](../use-cases/agent-reads-github-writes-need-approval.md) | |
| T3 | [UC4 Trust your terminal, not the agent](../use-cases/trust-terminal-not-agent.md) | |
| T4 | [UC5 One-shot secret for a script](../use-cases/one-shot-secret-for-script.md) | |
| T5 | [UC6 Gate git, az, and docker](../use-cases/gate-git-az-docker.md), with `--strong` for docker and gh | |
| T6 | [UC7 Audit and scan](../use-cases/audit-and-scan.md) | |
| T7 | [UC10 Gate npm](../use-cases/gate-npm.md) | |
| T8 | [UC11 Gate the AWS CLI](../use-cases/gate-aws.md) | |
| T9 | [UC12 Gate kubectl](../use-cases/gate-kubectl.md) | |
| T10 | [UC13 Gate ssh key use](../use-cases/gate-ssh-keys.md) | |
| T11 | [UC14 Short-lived GitHub tokens](../use-cases/short-lived-github-tokens.md) | |
| T12 | [UC15 Keep API keys out of the agent](../use-cases/api-keys-through-proxy.md) | |
| T13 | [UC16 Keep .env secrets out of the agent](../use-cases/env-files.md) | |

### 2.5 CmdWarden Vault app and tray icon

| ID | Steps | Expect | Result |
|----|-------|--------|--------|
| V1 | Open **CmdWarden Vault** from the Start Menu. Open each of the six pages. | Each page matches its screen in [CmdWarden Vault](../vault.md). No cut text. No empty page with no message. | |
| V2 | Do [UC9](../use-cases/vault-add-remove-secrets.md): add a secret with **Ctrl+N**, then delete it with **Del**. | `cw inject` can use the new secret. After the delete, `cw inject` says that the secret is not there. | |
| V3 | On **Secret Gates**, enroll with **Ctrl+E**, change a level, and unenroll with **Del**. | `cw policy list` shows each change. | |
| V4 | Run `cw agent stop`. Look at the app. | A yellow banner says that the Session Agent is not running. Actions are off. | |
| V5 | Resize the window from small to full screen. | The layout stays clean at each size. | |
| V6 | Open the tray icon menu after some cards. | The count of answered cards is correct. Live session allows show, and you can revoke one. | |

### 2.6 Update and uninstall

| ID | Steps | Expect | Result |
|----|-------|--------|--------|
| U1 | Install the previous release. Run `cw update`. | The new version installs. `cw version` shows it. Policy and secrets stay. | |
| U2 | Run `cw uninstall`. Keep the secrets when it asks. | Each step prints a result. `gh auth status` works with the stock login. `%LOCALAPPDATA%\CmdWarden` is gone. No CmdWarden folder is on PATH. The secrets stay in Credential Manager. | |
| U3 | Install again. Run `cw setup`. | Setup works. The kept secrets show on the **Secrets** page. | |

---

## Part 3 - Why some tests stay manual

A script cannot do these tests. This is on purpose, or it needs a real outside service.

| Area | Why a script cannot test it |
|------|-----------------------------|
| Approve on the real card (G4, G5, G8 to G12) | The card accepts Approve only from a real keyboard or mouse ([#23](https://github.com/BasantPandey/CmdWarden/issues/23)). An agent that can click Approve can approve its own request. The test `ApprovalGateInjectedInputTests` proves that scripts cannot click Approve. So only a person can prove that a real click works. |
| Windows Hello (G14, G15) | Hello needs a real face, finger, or PIN. |
| Look of the screens | A script can take a screenshot (A4). A person must decide if it looks right. |
| AI harness behavior (H1 to H6) | The harness and its model change often. A script cannot tell if the agent reads the deny message correctly. |
| Real logins (T1 to T13) | GitHub, npm, AWS, Azure, Docker, and kubectl need real test accounts. |
| Clean install and uninstall (M1, M2, U1 to U3) | These change PATH, Settings > Apps, and Credential Manager. Use a clean machine. |

The automated tests replace the card with a scripted gate (`CW_APPROVAL_MODE=allow|deny|session`). They also use a private pipe, vault, and folder (`CW_PIPE_NAME`, `CW_VAULT_ROOT`, `CW_PRODUCT_ROOT`, `CW_HOME`). So they test all the policy logic, but not the real click.
