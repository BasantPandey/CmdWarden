# CmdWarden Vault desktop app

CmdWarden Vault is a Windows desktop window. It reads the same policy file, vault, and audit trail as the CLI. It never shows a secret value. Open it from the Start Menu entry **CmdWarden Vault** or the Desktop icon.

```powershell
cw shortcut status              # where each shortcut is, and if it exists
cw shortcut install             # create the Start Menu entry
cw shortcut install --desktop   # create the Start Menu entry and the Desktop icon
cw shortcut remove              # delete both
```

Run `cw shortcut install` again after every reinstall of the tool. The shortcuts point at the exe inside the tool folder.

## Window layout

- **Left nav:** six page buttons. Click **«** or press **[** / **]** to collapse the nav to an icon rail.
- **Title bar:** page name, a **Session Agent** badge (Up / Agent down), and one primary button (**+ Add secret** on Secrets, **Run scan** on Detectors, **Refresh** elsewhere).
- **Body:** the page content. Every page is read-only except Secrets.

## Keys

Each button shows its key next to its label.

| Key | Where | Does |
|-----|-------|------|
| **F5** | Every page | Refresh the page. On Detectors, run a scan. |
| **Ctrl+N** | Secrets | Open **Add secret**. |
| **Down** / **Up** | Secrets | Select the next or the previous card. |
| **Del** | Secrets | Delete the selected card. A dialog asks you first. |
| **Esc** | Secrets | Clear the selection. |
| **[** / **]** | Every page | Collapse or expand the nav. |
| **Enter** | Add secret, Delete secret | **Save** or **Delete**. |
| **Esc** | Add secret, Delete secret | **Cancel**. Nothing changes. |

## Pages

| Page | Shows | CLI twin |
|------|-------|----------|
| **Secret Gates** | **Defaults** card: the level each launcher kind gets (AI Harness → Read, Terminal → Trusted). One card per enrolled launcher: kind pill, policy key, path, and which command classes auto-allow or go to the Approval Gate. **Active session allows** card: every live "Allow for session" grant with granted, last used, and expires times. | `cw policy list`, `cw policy sessions` |
| **Detectors** | Residual risk findings: title, tool, severity pill, summary, evidence, and remediation. Empty state: **Nothing to fix**. | `cw scan` |
| **Hardened Tools** | One card per catalog tool (`gh`, `git`, `az`, `docker`): pinned path and a pill **Hardened**, **Degraded**, or **Not hardened** with the reason. Strong mode shows in the note. | `cw harden --list`, `cw doctor` |
| **Secrets** | Every secret name in the vault. **+ Add secret** opens a dialog. **Delete** on a selected card asks for confirmation. | `cw save`, `cw delete` |
| **Secret Usage** | Recent gate decisions: time, launcher, tool · class, secret name, decision pill (**Auto-allow**, **Approved**, **Session granted**, **Session allow**, **Denied**), and reason code. | `cw audit` |
| **Doctor** | Cards for **Session Agent** (pipe, version), **Vault UI binary**, **Start Menu shortcut**, and a **Details** list. A **Version mismatch** pill means the agent binary is older than the app. | `cw doctor` |

## Screens

**Secret Gates** - defaults per launcher kind, one card per enrolled launcher, and active session allows.

![Secret Gates page](images/vault-secret-gates.png)

**Detectors** - one card per finding with severity, evidence, and the `cw` command that fixes it.

![Detectors page](images/vault-detectors.png)

**Hardened Tools** - one card per catalog tool with its harden state.

![Hardened Tools page](images/vault-hardened-tools.png)

**Secrets** - every secret name. Select a card with a click or the arrow keys, then click **Delete** or press **Del**.

![Secrets page](images/vault-secrets.png)

**Add secret** - type a name and a value. Press **Enter** to save or **Esc** to cancel.

![Add secret dialog](images/vault-add-secret.png)

**Delete secret** - press **Enter** to delete or **Esc** to keep the secret.

![Delete secret dialog](images/vault-delete-secret.png)

**Secret Usage** - the newest gate decisions with launcher, tool, secret name, and decision pill.

![Secret Usage page](images/vault-secret-usage.png)

**Doctor** - agent health, binary and shortcut checks, and the details list.

![Doctor page](images/vault-doctor.png)

## What the Vault does not do

- It does not enroll launchers or set policy levels. Use `cw policy enroll` and `cw policy set`.
- It does not harden tools. Use `cw harden`.
- It does not revoke session allows. Use `cw policy sessions --revoke <id>`.
- It does not show the Approval Gate. That card comes from the Session Agent when a gated command runs.

Each page prints the CLI command for the action it cannot do.
