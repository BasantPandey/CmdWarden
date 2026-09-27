# Research: what similar secret tools offer

Ticket: [#58](https://github.com/BasantPandey/CmdWarden/issues/58). Map: [#55](https://github.com/BasantPandey/CmdWarden/issues/55).
Date of research: 2026-09-27. All facts come from the first-party docs, READMEs, and release pages in [Sources](#sources).

## Question

What do similar tools offer? Which features do users expect as standard?
Compare CmdWarden with Infisical Agent Vault, 1Password CLI, Doppler, Bitwarden Secrets Manager, and new AI-agent secret brokers.
For each tool, find the install steps, the first-run steps, the core commands, and how it keeps secrets away from an agent.

## Answer

- Every tool puts the secret only into a child process or into a proxy. None gives the raw value to the agent by default.
- No other tool gates a secret by **tool + launcher**. They scope by token, vault, or host. The launcher gate is CmdWarden's own edge.
- Only 1Password CLI, Doppler, `bws`, and Bitwarden `aac` ship a Windows build. Agent Vault, OneCLI, and the small brokers do not.
- Users expect three things as standard: a one-line install (`winget`), a first run in 3 steps, and `run -- <cmd>` with an env file of secret references.
- 1Password `op run` masks secret values in the output by default. `cw inject` does not.
- Agent Vault lets the agent ask for a missing credential, and a human fills in the value. CmdWarden has no such path.
- CmdWarden needs the .NET 10 SDK, a zip, and 4 commands before the first block. This is the largest gap for the 2-minute value test.

## Comparison table

| Tool | Install on Windows | First run | Core commands | How the agent is kept from the secret | Human approval per use | Windows |
|------|--------------------|-----------|---------------|----------------------------------------|------------------------|---------|
| **CmdWarden** 0.7 | Zip + `install.cmd`; needs .NET 10 SDK | `cw doctor`, `cw policy enroll --kind terminal`, `cw policy enroll --kind ai-harness`, `cw harden gh`, new terminal | `cw harden`, `cw inject +NAME -- cmd`, `cw proxy`, `cw mcp`, `cw leak-guard`, `cw audit` | PATH shim + Session Agent gate by tool and launcher; child env only; proxy puts key in place of `cw://NAME` | Yes, Approval Gate card, Windows Hello option | Native, Windows only |
| **Infisical Agent Vault** v0.39.3 (preview) | None. Script for macOS and Linux, or Docker | Set `AGENT_VAULT_MASTER_PASSWORD`, `agent-vault server -d`, create owner in web UI, add vault, credentials, service rules, agent token | `agent-vault server`, `agent-vault run -- claude`, `agent-vault vault proposal approve <id>` | HTTPS proxy with TLS interception; agent sends a dummy value such as `__anthropic_api_key__`; proxy puts in the real key | No per-request card. Agent "proposals" for new credentials or services need a human approve | No build |
| **1Password CLI** (`op`) | `winget install 1password-cli` | Turn on Windows Hello in the app, turn on "Integrate with 1Password CLI", run `op vault list` | `op run --env-file=.env -- cmd`, `op inject -i tpl -o file`, `op read` | `op://vault/item/field` references resolve only in the child env; output masked by default | Yes, per terminal session; ends after 10 idle minutes, hard limit 12 hours | Native |
| **1Password SSH agent** | Part of the 1Password app | Disable the Windows OpenSSH Authentication Agent service, turn on "Use the SSH Agent", set `core.sshCommand` | Standard `ssh` and `git` | Private key never leaves the app | Yes, per client; approval lasts until 1Password locks, time is configurable | Native, takes `\\.\pipe\openssh-ssh-agent` |
| **1Password for Claude** (GA 2026-07-16) | 1Password desktop app + browser extension | Connect in Claude desktop or Claude in Chrome | None (browser autofill) | 1Password fills the login into the page; the model never sees it | Yes, biometric per request; scope ends with the task | Mac only at launch |
| **Doppler** | `winget install doppler.doppler` or Scoop | `doppler login` (browser), `doppler setup` per project | `doppler run -- cmd`, `doppler secrets get NAME --plain`, MCP server `@dopplerhq/mcp-server` | Scoped, read-only, expiring service token; `doppler run` puts secrets in the agent process env | No. Scope and audit log only | Native |
| **Bitwarden Secrets Manager** (`bws`) | `iwr https://bws.bitwarden.com/install \| iex` | Make a machine account access token, set `BWS_ACCESS_TOKEN` | `bws run -- cmd`, `bws secret list`, `bws secret get <id>` | Child env from `bws run`; scope comes from the access token | No | Native |
| **Bitwarden Agent Access** `aac` v0.11.0 (alpha) | `aac-windows-x86_64.zip` on PATH | `aac listen` on the vault side, `aac connect` with a pairing token | `aac run --domain <d> --env VAR=field -- cmd` | Encrypted tunnel; value goes only to the child env, "never touch stdout or disk" | Yes, user approves each credential request in the CLI | Native build |
| **OneCLI** (open source, Apache-2.0 core) | None documented; self-host with `pnpm`, or cloud | `pnpm install`, `pnpm run setup`, open `localhost:10254` | Web UI and gateway | Rust HTTPS gateway with TLS interception; agent holds placeholder keys only | Human-in-the-loop in chat for sensitive actions | Not documented |

Two small 2026 projects show the same pattern: [Brissux-Labs/agent-secrets](https://github.com/Brissux-Labs/agent-secrets) (macOS, `agent-secrets run -- cmd`, MCP that shows names but never values) and [R055LE/secrets-broker](https://github.com/R055LE/secrets-broker) (Linux, a human approves each request on a separate device). Neither supports Windows.

## Notes per tool

### Infisical Agent Vault

- It is a local HTTP and HTTPS proxy in Go. It "terminates TLS" and acts as the upstream service. ([blog](https://infisical.com/blog/agent-vault-the-open-source-credential-proxy-and-vault-for-agents))
- The agent gets `HTTPS_PROXY` and a CA certificate. It sends a dummy value, and the proxy puts in the real credential. ([README](https://github.com/Infisical/agent-vault))
- An admin must define each service as a host rule, for example `api.stripe.com`. ([blog](https://infisical.com/blog/agent-vault-the-open-source-credential-proxy-and-vault-for-agents))
- An agent can raise a **proposal** to add a credential or a service. A vault member fills in the value and approves it in the browser or with `agent-vault vault proposal approve <id>`. ([proposals](https://docs.agent-vault.dev/learn/proposals))
- It keeps a request log of brokered traffic. ([README](https://github.com/Infisical/agent-vault))
- The install page lists macOS, Linux, Docker, and source builds only. Release v0.39.3 (2026-09-01) has no Windows asset. ([install](https://docs.agent-vault.dev/installation), [release](https://github.com/Infisical/agent-vault/releases))
- CmdWarden already has the same idea in `cw proxy` with `cw://NAME` placeholders.

### 1Password CLI, SSH agent, and 1Password for Claude

- `op run` resolves `op://` references from exported vars, an `--env-file`, or a 1Password Environment. It runs the command in a subprocess. ([op run](https://www.1password.dev/cli/secrets-environment-variables/))
- "By default, `op run` masks secret values in stdout." The flag `--no-masking` turns this off. ([op run](https://www.1password.dev/cli/secrets-environment-variables/))
- `op inject` writes a resolved config file from a template. The docs warn you to delete the resolved file. ([op inject](https://www.1password.dev/cli/secrets-config-files/))
- CLI approval is per terminal session. On Windows, the session is the PID and start time of the calling process. A sub-shell needs a new approval. It ends after 10 idle minutes, with a hard limit of 12 hours. ([app integration security](https://www.1password.dev/cli/app-integration-security/))
- First run on Windows: `winget install 1password-cli`, turn on Windows Hello, turn on the CLI integration, run `op vault list`. ([get started](https://www.1password.dev/cli/get-started/))
- The SSH agent needs you to disable the Windows OpenSSH Authentication Agent service. 1Password then listens on `\\.\pipe\openssh-ssh-agent`. ([SSH get started](https://www.1password.dev/ssh/get-started/))
- SSH approvals are per client and last until 1Password locks. You can set how long it remembers them. ([SSH agent](https://www.1password.dev/ssh/agent/))
- For agents, 1Password tells you to use a service account with read access to one vault, and a usage report for audit. ([AI agent tutorial](https://www.1password.dev/sdks/ai-agent))
- 1Password for Claude (2026-07-16) fills website logins into the page after a biometric approval. Access "is scoped to the current task." It is Mac only at launch and covers the browser, not the CLI. ([blog](https://1password.com/blog/1password-for-claude))

### Doppler

- Install: `winget install doppler.doppler`. First run: `doppler login`, then `doppler setup` once per project folder. ([install](https://docs.doppler.com/docs/install-cli))
- `doppler run -- cmd` puts secrets into the child env. ([install](https://docs.doppler.com/docs/install-cli))
- For agents, Doppler tells you to make one config per agent and a "scoped, read-only, expiring service token." "Every secret access is logged." ([agents](https://www.doppler.com/agents))
- The Doppler MCP server can "read secret values." Its `--read-only` flag hides write tools but does not stop reads. ([MCP](https://docs.doppler.com/docs/mcp))
- There is no per-use human approval. The control is the token scope.

### Bitwarden Secrets Manager and Agent Access

- `bws` install on Windows: `iwr https://bws.bitwarden.com/install | iex`. Auth: set `BWS_ACCESS_TOKEN`. ([bws CLI](https://bitwarden.com/help/secrets-manager-cli/))
- `bws run -- cmd` puts secrets into the child env. `bws` keeps a state file with auth data under `~/.config/bws/state` by default. ([bws CLI](https://bitwarden.com/help/secrets-manager-cli/))
- Agent Access (`aac`) came out on 2026-03-24 as an early alpha. The user approves each credential request in the CLI. ([blog](https://bitwarden.com/blog/introducing-agent-access-sdk/))
- `aac run --domain <d> --env VAR=field -- cmd` maps a vault field to an env name you choose. Release v0.11.0 ships a Windows zip. ([repo](https://github.com/bitwarden/agent-access), [release](https://github.com/bitwarden/agent-access/releases))

### OneCLI

- OneCLI is now "an open-source platform for running AI agents as a team." A Rust gateway uses TLS interception and injects credentials. ([repo](https://github.com/onecli/onecli))
- It stores keys with AES-256-GCM and decrypts them only at request time. It can pull from Bitwarden or 1Password. ([repo](https://github.com/onecli/onecli))
- It is a server product for teams, not a tool for one Windows developer.

## What users expect as standard

These features show up in 3 or more tools. A new user will look for them.

| Standard feature | Who has it | CmdWarden today |
|------------------|-------------|-----------------|
| One-line install from a package manager | 1Password (`winget`), Doppler (`winget`, Scoop), `bws` (`iwr \| iex`) | Zip + `install.cmd` + .NET 10 SDK. Winget and Scoop templates exist but are not public |
| First run in 3 steps or fewer | 1Password, Doppler, `bws` | 4 commands + a new terminal before the first block |
| `run -- <cmd>` puts secrets only in the child env | `op run`, `doppler run`, `bws run`, `aac run`, `agent-vault run` | Yes: `cw inject +NAME -- cmd` |
| Env file of secret references | `op run --env-file`, `op inject`, `aac run --env VAR=field` | No. One `+NAME` per secret, and the env name must equal the secret name |
| Mask secret values in the child output | `op run` (default on) | Only the Claude and Cursor leak-guard hook. `cw inject` output is not masked |
| Scoped, time-limited access | 1Password (10 min idle, 12 h), Doppler (expiring tokens), 1Password for Claude (per task) | Yes: session allow with 10 min, 1 h, or 60 idle min |
| Audit of each secret use | Doppler, Agent Vault, 1Password usage report | Yes: `cw audit`, and release fails closed if audit cannot write |
| Agent can ask for a missing secret | Agent Vault proposals, `aac` pairing | No |

## Where CmdWarden is ahead

- Policy by **tool + launcher**. Your terminal and your AI harness get different levels for the same tool. No other tool does this.
- A **command class** (read, write, secret-reveal) per call. The others scope by token or host only.
- A per-use Approval Gate on Windows with Windows Hello. Only 1Password (Mac for the Claude feature) and alpha `aac` ask per use.
- Gates the CLI tools a developer already uses (`gh`, `git`, `az`, `docker`, `npm`, `aws`, `kubectl`, `ssh`). The others need you to move secrets into their store first.
- Sits in front of the Windows OpenSSH agent. 1Password must replace it.
- No account, no cloud, no server, no master password.

## Gaps in CmdWarden

1. **Install weight.** It needs the .NET 10 SDK and a manual zip. Competitors install with one `winget` line.
2. **Time to first block.** A new user must enroll 2 launchers, harden a tool, and open a new terminal. No command shows a block on its own.
3. **No output masking in `cw inject`.** A child that prints its env shows the secret to the agent. `op run` masks by default.
4. **No env file or env-name mapping.** Users of `op run --env-file` or `.env` files must rewrite their flow. `cw inject` cannot map vault `GH_PAT` to env `GITHUB_TOKEN`.
5. **No way for the agent to ask for a missing secret.** The agent must tell the user to run `cw save`. Agent Vault turns this into an approve flow.
6. **Weekly value is not visible.** `cw audit -n N` lists rows. There is no summary of what CmdWarden blocked this week.

## Candidate features for v0.8.0

Ranked by the value test: a new user sees a real block in under 2 minutes, and keeps CmdWarden on after one week.

| Rank | Candidate | What it does | Evidence | Value test |
|------|-----------|--------------|----------|------------|
| 1 | `cw try` | One command. It enrolls the current terminal, hardens `gh` if needed, then starts a child as an ai-harness launcher that runs a write, so the real Approval Gate card shows. It then prints the audit row and how to undo | 1Password, Doppler, and `bws` reach first use in 3 steps; CmdWarden needs 4 commands + a new terminal | First block in under 2 min |
| 2 | Public `winget` and Scoop package with a self-contained build | `winget install CmdWarden`. No .NET SDK on the PC | `winget install 1password-cli`, `winget install doppler.doppler`, `bws` one-line install | Cuts install time; removes the SDK step |
| 3 | Mask output in `cw inject` | Replace each released value with `[CmdWarden: NAME]` in the child stdout and stderr. `--no-masking` turns it off | `op run` masks by default | Stops the easy leak through `env` or `echo`; trust after one week |
| 4 | `cw inject --env-file <file>` and `VAR=cw://NAME` | Read `KEY=cw://NAME` lines and map a vault name to any env name. Same gate and one card for the whole set | `op run --env-file` with `op://`, `aac run --env VAR=field`; `cw://NAME` already exists for `cw proxy` | Lets `.env` users switch without new habits |
| 5 | MCP tool `request_secret` | The agent asks for a secret by name and reason. The Approval Gate asks the user to type the value, saves it to the vault, and returns only the name | Agent Vault proposals; agent-secrets one-time form; today the agent must ask the user to run `cw save` | Keeps the agent flow smooth, so the user keeps CmdWarden on |
| 6 | `cw audit --week` | A short summary: blocks, prompts, and approvals per launcher and tool for the last 7 days | Doppler and Agent Vault show a log of each access | Shows value at day 7 |

Not recommended for v0.8.0:

- A full HTTPS gateway like Agent Vault or OneCLI. `cw proxy` already covers API keys, and a server does not fit one Windows developer.
- Browser autofill like 1Password for Claude. It is outside the CLI scope of CmdWarden.
- A cloud sync of secrets like Doppler or `bws`. It adds an account, and CmdWarden's edge is "no account."

## Sources

Accessed 2026-09-27.

- Infisical Agent Vault: [README](https://github.com/Infisical/agent-vault), [releases](https://github.com/Infisical/agent-vault/releases), [installation](https://docs.agent-vault.dev/installation), [proposals](https://docs.agent-vault.dev/learn/proposals), [launch blog, 2026-04-22](https://infisical.com/blog/agent-vault-the-open-source-credential-proxy-and-vault-for-agents)
- 1Password: [CLI get started](https://www.1password.dev/cli/get-started/), [op run](https://www.1password.dev/cli/secrets-environment-variables/), [op inject](https://www.1password.dev/cli/secrets-config-files/), [app integration security](https://www.1password.dev/cli/app-integration-security/), [SSH agent](https://www.1password.dev/ssh/agent/), [SSH get started](https://www.1password.dev/ssh/get-started/), [SDK with AI agents](https://www.1password.dev/sdks/ai-agent), [1Password for Claude](https://1password.com/blog/1password-for-claude)
- Doppler: [install CLI](https://docs.doppler.com/docs/install-cli), [AI agents](https://www.doppler.com/agents), [MCP server](https://docs.doppler.com/docs/mcp)
- Bitwarden: [Secrets Manager CLI](https://bitwarden.com/help/secrets-manager-cli/), [Agent Access repo](https://github.com/bitwarden/agent-access), [Agent Access releases](https://github.com/bitwarden/agent-access/releases), [Agent Access blog, 2026-03-24](https://bitwarden.com/blog/introducing-agent-access-sdk/)
- OneCLI: [repo](https://github.com/onecli/onecli)
- Small brokers: [Brissux-Labs/agent-secrets](https://github.com/Brissux-Labs/agent-secrets), [R055LE/secrets-broker](https://github.com/R055LE/secrets-broker)
- CmdWarden: [CLI reference](../cli-reference.md), [install](../install.md), `src/CmdWarden.Cli/Program.cs` (`InjectAsync`), `src/CmdWarden.Contracts/ApprovalOutcome.cs` (session lengths)
