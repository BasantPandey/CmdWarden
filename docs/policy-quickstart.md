# Policy quick start

Good policy for `gh`, `git`, `az`, `docker`, and your own scripts, without reading the full [user guide](user-guide.md). Copy one block, pick one profile, work.

Install first: [install.md](install.md). Terms: [Glossary](glossary.md).

---

## 1. One-minute setup

Run the first block in **your normal terminal** (Windows Terminal / PowerShell).

```powershell
cw doctor
cw policy enroll --kind terminal
cw harden gh
cw harden git
cw harden az
cw harden docker
```

Run the second block in the **AI harness terminal** (Cursor / Claude Code / Codex panel).

```powershell
cw policy enroll --kind ai-harness
```

Open a **new** terminal, then check:

```powershell
where.exe gh
cw policy list
```

*Expect:* `cw doctor` shows Session Agent **UP**. `where.exe gh` shows the CmdWarden shim first. `cw policy list` shows two launchers: one **terminal**, one **ai_harness**.

You are done. The defaults are the **Recommended** profile below. You do not need `cw policy set` for them.

---

## 2. Pick a profile

Policy is per **tool x launcher**. The `"*"` tool name sets one level for every tool on one launcher.

| Profile | Terminal | AI harness | Use when |
|---------|----------|------------|----------|
| **Recommended** (default) | Trusted | Read | Most people. The agent can list and view. Writes and secret reveal prompt you. |
| **Cautious** | Trusted | Deny | Every agent tool call prompts you. Good for a demo or an untrusted repo. |
| **Open** | Full | Trusted | Solo machine. You accept agent writes. Secret reveal still prompts for the agent. |

Find your keys first:

```powershell
cw policy list
# <terminalKey> = the key with kind: terminal
# <harnessKey>  = the key with kind: ai_harness
```

**Recommended** - nothing to set. To return to it after a change:

```powershell
cw policy set <terminalKey> "*" Trusted
cw policy set <harnessKey> "*" Read
```

**Cautious**

```powershell
cw policy set <harnessKey> "*" Deny
```

**Open**

```powershell
cw policy set <terminalKey> "*" Full
cw policy set <harnessKey> "*" Trusted
```

*Expect:* `cw policy list` shows a `*: <level>` row under the launcher. Changes apply at once. No restart.

---

## 3. What each level auto-allows

Every command gets one **command class**: **read**, **write**, **secret-reveal**, or **unknown**.

| Level | Auto-allows | Opens the Approval Gate |
|-------|-------------|-------------------------|
| **Deny** | nothing | everything |
| **Read** | read | write, secret-reveal, unknown |
| **Trusted** | read, write | secret-reveal, unknown |
| **Full** | everything | nothing |

The Approval Gate is a desktop card with **Deny**, **Allow for session**, and **Approve Once**. Both allow buttons last until the launcher exits or 60 idle minutes pass. **Approve Once** covers the one command class you saw. **Allow for session** covers that class and every lower one. Neither covers secret-reveal. If no desktop is available, the request **fails closed**.

The tables below show how CmdWarden classifies each tool. Help and version flags are always **read**.

### gh

| Class | Examples |
|-------|----------|
| read | `pr list`, `issue view`, `repo view`, `run list`, `release download`, `auth status`, `status`, `browse` |
| write | `pr create`, `pr merge`, `issue close`, `release create`, `workflow run`, `auth login`, `auth logout`, **`api` (all calls)** |
| secret-reveal | `auth token`, `auth git-credential get`, any command with `--show-token` |
| unknown | a bare group such as `gh pr`, an unlisted verb, an extension command |

### git

| Class | Examples |
|-------|----------|
| read | `status`, `log`, `diff`, `show`, `branch`, `fetch`, `pull`, `clone`, `ls-remote`, `config --get`, `config --list`, `remote -v`, `tag` (list) |
| write | `push`, `commit`, `add`, `checkout`, `merge`, `rebase`, `reset`, `stash`, `config <key> <value>`, `remote add`, `remote set-url`, `tag v1.0` |
| secret-reveal | `credential fill`, `credential get`, `credential-manager get`, any `-c credential.*`, `-c http.extraHeader`, `-c core.askpass` |
| unknown | an unlisted command such as `git lfs` or a custom alias |

### az

The **last** command word decides the class.

| Class | Examples |
|-------|----------|
| read | `account show`, `group list`, `vm show`, `webapp log tail`, `storage blob download`, `... wait` |
| write | `login`, `logout`, `account clear`, `configure`, `extension add`, `rest`, `group create`, `vm start`, `webapp deploy`, `role assignment create` |
| secret-reveal | `account get-access-token`, `ad sp create-for-rbac`, `ad sp credential reset`, `keyvault secret show`, `keyvault secret download`, `keyvault secret backup`, `storage account keys list`, `storage account keys renew` |
| unknown | a bare group such as `az group`, an unlisted verb |

### docker

docker has **no secret-reveal class**. `Trusted` auto-allows every classified docker command.

| Class | Examples |
|-------|----------|
| read | `ps`, `logs`, `images`, `inspect`, `info`, `pull`, `system df`, `network ls`, `volume ls`, `compose ps`, `compose logs` |
| write | `login`, `logout`, `push`, `run`, `build`, `exec`, `rm`, `system prune`, `network create`, `compose up`, `compose down`, `buildx build` |
| unknown | a bare group such as `docker network`, an unlisted plugin such as `docker scout` |

---

## 4. Per-tool overrides

A tool row beats the `"*"` row on the same launcher. Set one tool, keep the rest:

```powershell
# Agent may run docker builds without a prompt; gh, git, az stay at the "*" level
cw policy set <harnessKey> docker Trusted

# Agent may never touch az, not even list
cw policy set <harnessKey> az Deny

# Your terminal may run gh auth token without a prompt
cw policy set <terminalKey> gh Full
```

*Expect:* `cw policy list` shows the tool row next to the `*` row.

---

## 5. Other tools and scripts

Any script or CLI can get a vault secret through `cw inject`. The default tool name is `inject` and the default class is **write**. An agent at **Read** always gets a prompt for inject. That is on purpose.

Give a script its own tool name and class, then set a level for it:

```powershell
cw save DEPLOY_KEY
cw inject --tool deploy --class read +DEPLOY_KEY -- cmd /c my-deploy.exe --check
cw policy set <harnessKey> deploy Read
```

*Expect:* the agent runs `cw inject --tool deploy --class read ...` without a prompt. A call with `--class write` or without `--tool deploy` still prompts. The secret goes only into the child process.

Do not use inject in place of `cw harden gh`. Harden owns `GH_TOKEN` in the Vault and releases it per run.

---

## 6. Strong mode (optional)

Compat mode (the default above) gates the run and leaves the tool's own credential store in place. Strong mode moves the credentials into the Vault and deletes the originals.

```powershell
cw harden gh --strong      # gh keyring token -> Vault; child gets GH_TOKEN per run
cw harden git --strong     # .git-credentials -> Vault; git uses git-credential-cmdwarden
cw harden docker --strong  # config.json auths -> Vault; docker uses docker-credential-cmdwarden
```

`az` has no strong mode. It stays compat. `cw unharden <tool>` writes the originals back. Detail: [UC6](use-cases/gate-git-az-docker.md).

---

## 7. Change your mind

```powershell
cw policy list                          # keys, kinds, levels
cw policy set <key> <tool> <level>      # tool: gh | git | az | docker | inject | <yours> | "*"
cw policy sessions                      # active "Allow for session" grants
cw policy sessions --revoke-all         # end all grants now
cw policy unenroll <key>                # launcher goes back to Deny
```

The **Secret Gates** page of CmdWarden Vault shows the same launchers and levels. The **Secret Usage** page shows every decision.
