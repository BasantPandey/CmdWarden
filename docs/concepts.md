# How CmdWarden works

Your AI harness can run shell commands. Those commands can call `gh`, `git`, `az`, or `docker` with **your** credentials - list private repos, open PRs, push code, talk to cloud APIs. Without a gate, anything that can spawn a process on your machine can often use the same tokens your terminal already has.

**CmdWarden** sits between the launcher (terminal or AI harness) and the hardened tool:

1. You **enroll** which launchers you trust, and how much.
2. You **harden** tools so PATH hits a **Shim** that asks the **Session Agent** first.
3. Secrets leave the **Vault** only into the **child** process for that run (when policy allows), not into every agent session forever.
4. When policy is unsure, you get an **Approval Gate** (Allow once / Deny). If the UI cannot show, the request **fails closed**.

Install is binaries only. Day-to-day you still type `gh` and `git` as usual after setup.

CmdWarden has three user surfaces:

| Surface | What it is | When you see it |
|---------|------------|-----------------|
| **`cw` CLI** | Setup and power-user commands | You type them in a terminal |
| **Approval Gate** | Small desktop card with **Deny** (Esc) / **Allow for session** (A) / **Approve Once** (Enter) | Pops up while a gated command waits |
| **CmdWarden Vault** | Desktop app with six pages: Secret Gates, Detectors, Hardened Tools, Secrets, Secret Usage, Doctor | You open it from the Start Menu |

The [CmdWarden Vault](vault.md) section describes every page. Each use case below names the Vault page that shows the same information.

## Concepts

| Idea | Plain meaning |
|------|----------------|
| **Session Agent** | Background process per user that evaluates policy and can show the Approval Gate |
| **Launcher** | Who started the tool (terminal vs AI harness), from the process chain |
| **Enroll** | Register a launcher as terminal or ai-harness so defaults apply |
| **Harden** | Opt-in: pin real binary + PATH shim (+ vault token for `gh`) |
| **Policy level** | Deny / Read / Trusted / Full for a **tool × launcher** pair |
| **Approval Gate** | Desktop **Deny / Allow for session / Approve Once** card when auto-allow does not apply (appears while the agent tool call waits) |
| **Vault** | Secrets in Windows Credential Manager, released only to allowed children |
| **CmdWarden Vault** | Desktop app (Start Menu) to view gates, detectors, hardened tools, secret names, usage, and doctor |
| **Session allow** | An Approval Gate grant that lasts until the launcher exits or 60 idle minutes. **Approve Once** grants the one class you saw; **Allow for session** grants that class and every lower one |

Enrollment is by **identity key** from `cw whoami`, not by brand name. Cursor, Claude Code, and Codex are examples of the **ai-harness** kind - same enroll commands for each.

`cw whoami` prints the identity CmdWarden sees for the current shell:

![cw whoami output: caller pid, launcher kind, policy key, path, and the process chain](images/cli-whoami.svg)

Escape if `whoami` is wrong or unknown:

```powershell
cw policy enroll --kind ai-harness --key <policyKey>
```

Agent lifecycle:

```powershell
cw doctor
cw agent start
cw agent status
cw agent stop
```

Many commands lazy-start the agent; prefer `cw doctor` after install.

## Day-to-day

1. Use `gh` / `git` / … **via PATH** in a **new shell** after harden (not absolute path to the real binary - that bypasses the shim).
2. Work normally. When the Approval Gate appears, click **Approve Once**, **Allow for session**, or **Deny**.
3. Let the agent say why it runs a command. Set `CW_REASON` for the command, for example `CW_REASON="create the release PR" gh pr create`. The Approval Gate shows the text under the command, with the label **The agent says:**. The audit stores it. CmdWarden never treats it as proof, and policy never reads it. The text is one line of 200 characters or less.
4. Stop an agent before it runs a command that CmdWarden will deny. Run `cw hook install claude` or `cw hook install cursor` once. After you deny a tool, the agent gets the deny before the next try, and not a failed command.
5. Tighten with `cw policy set` only when defaults are not enough ([UC3](use-cases/agent-reads-github-writes-need-approval.md)).
6. Periodically open CmdWarden Vault (**Detectors**, **Secret Usage**) or run `cw scan` / `cw audit` ([UC7](use-cases/audit-and-scan.md)).

Secrets reminder:

- **Track A (preferred for gh):** harden owns `GH_TOKEN` in the Vault; child-only release.
- **Track B:** `cw save` / `inject` / `delete`, or the Vault **Secrets** page, for named one-shot secrets ([UC5](use-cases/one-shot-secret-for-script.md), [UC9](use-cases/vault-add-remove-secrets.md)).
- **Not for this:** browser passwords, full keychain UI, enterprise cloud secret managers.
