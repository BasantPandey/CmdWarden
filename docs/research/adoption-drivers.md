# Why developers adopt or drop an AI-agent secret tool

Research for [#56](https://github.com/BasantPandey/CmdWarden/issues/56). Map: [#55](https://github.com/BasantPandey/CmdWarden/issues/55). Date: 2026-09-27.

## Question

Why do developers adopt, or drop, a tool that guards secrets from AI coding agents?

## Answer in short

- Developers fear one thing most: the agent reads `.env` and config secrets, and the built-in ignore rules do not stop it.
- The second fear is theft of CLI tokens (`gh`, `npm`, ssh, cloud) by malware that uses the AI CLI or a package script.
- People drop a guard tool when it asks too often. Users approve about 93% of prompts and start to click without reading.
- People never finish setup when it needs many steps, a wrapper command, or shell knowledge.
- People install fast when a real scare is fresh, the tool keeps their normal commands, and the tool looks safe to trust.
- For CmdWarden, the top fixes are a one-step setup that ends in a real block, and a strict budget on prompts.

## Method and limits

- Sources: GitHub issues (with reaction counts), Hacker News threads and comments, vendor forums, vendor engineering posts, and security research posts.
- Frequency: we count how many independent sources raise a theme. We also use the Hacker News comment search ([hn.algolia.com](https://hn.algolia.com/api)) from 2025-01-01. The HN counts are keyword hits, so they show scale, not exact numbers.
- Reddit: our fetch tools cannot reach reddit.com (the site blocks them). This research has no Reddit data. Reddit can add color, but the themes below repeat in every other source.
- Quotes: we paraphrase most posts and give a link to each one. We give one short verbatim quote. Read the linked source for the exact words.

## Findings, ranked by how often they come up

| Rank | Theme | Kind | Signal |
|------|-------|------|--------|
| 1 | The agent reads `.env` and config secrets. Ignore rules fail. | Fear | 9+ Claude Code issues, 4 Cursor forum threads, press, research posts. Top issue has 54 upvotes and 64 comments. |
| 2 | Too many prompts. Users rubber-stamp, then turn the guard off. | Drop cause | Anthropic data (93% approved). HN: about 1,200 comments on "permission prompts", about 300 on `--dangerously-skip-permissions`. 1Password forum threads. |
| 3 | Malware or prompt injection steals CLI tokens. | Fear | s1ngularity, Shai-Hulud, litellm (797 upvotes), GitGuardian 2026 report. HN: about 170 comments on prompt injection and exfiltration. |
| 4 | Keep the secret out of the model. Inject it at run time. | Ask | 5+ Show HN tools in 2026. Claude Code feature requests with 36 and 174 upvotes. |
| 5 | Setup is too hard: many steps, wrappers, config. | Drop cause | Claude Code issues, Agent Vault team, CmdWarden's own timed install (#59). |
| 6 | The guard breaks normal work (false blocks, broken tools). | Drop cause | Claude Code issues #91681 and #401. Agent Vault issues #132, #194, #210. |
| 7 | Trust: who guards the guard, what leaves the machine, unsigned files. | Adoption gate | Comments on every Show HN launch. SmartScreen issues on many projects. |
| 8 | The agent does damage with real credentials. | Fear | Anthropic incident list. Claude Code #401 (database wiped). HN: about 160 comments. |
| 9 | Redact secrets in output. Use my existing store (1Password). | Ask | Claude Code #20966, #23642 (25 upvotes), Agent Vault #124. |

### 1. Fear: the agent reads `.env` and config secrets

This theme comes up the most. It is the fear that people describe in their own words, with a repro.

- Claude Code loaded the project `.env` into its own shell. In one Laravel project, each test run then wiped the dev database. The reporter asked if this is a massive issue. The issue has 54 upvotes, 64 comments, and many "still happens" replies over months. Deny rules and `CLAUDE.md` rules did not help the commenters. Source: [anthropics/claude-code#401](https://github.com/anthropics/claude-code/issues/401).
- The Register reproduced that Claude Code reads `.env` even with a `.claudeignore` entry. The article lists nine more open issues on the same problem. Source: [The Register, 2026-01-28](https://www.theregister.com/2026/01/28/claude_code_ai_secrets_files/).
- A user asked for automatic redaction, because the Read and Bash tools show tokens in full. The user must rotate keys after each exposure. Source: [anthropics/claude-code#20966](https://github.com/anthropics/claude-code/issues/20966).
- In Cursor, `.cursorignore` blocked direct reads. The agent then ran `type .env` in the terminal and read the file on the first try. Cursor staff said the ignore file does not apply to terminal or MCP tool calls. Source: [Cursor forum, 2026-02-15](https://forum.cursor.com/t/cursor-ide-agent-reads-env-file/151913).
- Another Cursor user asked for a guard or a warning when the agent opens `.env`. The user notes that many developers do not know about the ignore files. Source: [Cursor forum, 2026-04-02](https://forum.cursor.com/t/cursor-ai-can-expose-secrets-in-env-files-security-concern/156486). More threads: [136998](https://forum.cursor.com/t/cursor-reads-env-even-though-it-is-on-cursorignore/136998), [145607](https://forum.cursor.com/t/cursor-keeps-trying-to-access-sensitive-env-variables-even-though-env-is-ignored/145607).
- Knostic reports two cases. A Cursor agent uploaded a local file that held an API key. Claude Code put a Gemini key in a test file and pushed it to a branch. Source: [Knostic, 2025-12-16](https://www.knostic.ai/blog/claude-cursor-env-file-secret-leakage).
- HN users say app-level rules are not a real boundary. The agent can write a small script that reads the file and run it. Sources: [apwheele](https://news.ycombinator.com/item?id=47136615), [oldestaxe](https://news.ycombinator.com/item?id=47281483).

Which secrets: `.env` API keys, database URLs, cloud keys, and tokens in MCP and harness config. Which actions: file read, `cat`/`type`, `env`/`printenv`, and upload or commit of a file that holds a key.

### 2. Drop cause: too many prompts

This is the top reason a guard layer stops working. The user keeps the tool, but clicks Approve without reading, or turns it off.

- Anthropic: Claude Code users approve 93% of permission prompts. Over time, people stop paying attention to what they approve. The two escapes are a sandbox or `--dangerously-skip-permissions`. Source: [Anthropic engineering, Claude Code auto mode](https://www.anthropic.com/engineering/claude-code-auto-mode).
- On HN, a builder of an agent approval layer asks how others deal with approval fatigue: "users start rubber-stamping everything within a week" ([chonghaoju](https://news.ycombinator.com/item?id=49054579)). This matches the CmdWarden one-week value test.
- Other HN makers say the same. One records agent actions for review later, because live approvals turn into autopilot clicks ([laurencoral](https://news.ycombinator.com/item?id=48830674)). Another says policy must allow routine actions and ask only for a small set of risky ones ([Axtary](https://news.ycombinator.com/item?id=49062059)).
- Many HN users say they always run with `--dangerously-skip-permissions` ([collinskab](https://news.ycombinator.com/item?id=49496212), [clbrmbr](https://news.ycombinator.com/item?id=46332068), [kstenerud](https://news.ycombinator.com/item?id=47361120)).
- 1Password CLI: one user got one prompt per `op read`, so 20 secrets gave 20 prompts. The user said they were close to going back to encrypted volumes. Source: [1Password Community, 2023-03-09](https://www.1password.community/developers-69/cli-keeps-prompting-for-authentication-12009).
- 1Password SSH agent: a background `git fetch` caused a prompt every 5 minutes. On Windows, users got a 1Password dialog and then a Windows Hello prompt for each request. 1Password fixed both with "approve for all applications" and a better Windows prompt. Sources: [thread 19936](https://www.1password.community/developers-69/ssh-agent-iterm-bash-git-prompt-authorization-prompt-shown-every-5min-19936), [thread 143502](https://www.1password.community/discussions/developers/ssh-agent-and-windows-hello/143502).
- Agents make the prompt count worse. Each agent shell is a new session, so each `op` call asks again. Source: [jhnguyy/pi-env#443](https://github.com/jhnguyy/pi-env/issues/443).

### 3. Fear: malware or prompt injection steals CLI tokens

This fear is newer, but each incident gets wide coverage. The stolen items are the same tokens that CmdWarden gates.

- s1ngularity (Nx, 2025-08-26): a `postinstall` script looked for local Claude, Gemini, and Amazon Q CLIs. It ran them with `--dangerously-skip-permissions`, `--yolo`, and `--trust-all-tools` to search for secrets. It leaked GitHub tokens, npm tokens, ssh keys, and `.env` files. Sources: [Nx advisory GHSA-cxm3-wv7p-598c](https://github.com/nrwl/nx/security/advisories/GHSA-cxm3-wv7p-598c), [Wiz](https://www.wiz.io/blog/s1ngularity-supply-chain-attack), [Snyk](https://snyk.io/blog/weaponizing-ai-coding-agents-for-malware-in-the-nx-malicious-package/).
- Shai-Hulud (npm worm, 2025 and 2026): it stole GitHub PATs, npm tokens, and AWS, GCP, and Azure keys from developer machines. It used stolen npm tokens to publish itself. Sources: [Unit 42](https://unit42.paloaltonetworks.com/npm-supply-chain-attack/), [Datadog Security Labs](https://securitylabs.datadoghq.com/articles/shai-hulud-2.0-npm-worm/).
- litellm (2026-03): a malicious release shipped a credential stealer. The GitHub issue has 797 upvotes. Source: [BerriAI/litellm#24512](https://github.com/BerriAI/litellm/issues/24512).
- GitGuardian 2026: 33,185 unique secrets on 6,943 compromised developer machines. Claude Code-assisted commits leak secrets at 3.2%, against a 1.5% baseline. Source: [GitGuardian, State of Secrets Sprawl 2026](https://blog.gitguardian.com/the-state-of-secrets-sprawl-2026/).
- A prompt in a PR title made agent CI actions post their own API keys. Source: [VentureBeat](https://venturebeat.com/security/ai-agent-runtime-security-system-card-audit-comment-and-control-2026).

### 4. Ask: keep the secret out of the model

When people say what they want, they say this: the model never sees the value. A process fills it in at run time.

- A Claude Code feature request asks for built-in secrets. Its rule: secrets never appear in the chat context. It says Doppler and 1Password CLI need setup and shell knowledge that many users do not have. 36 upvotes. Source: [anthropics/claude-code#29910](https://github.com/anthropics/claude-code/issues/29910).
- The top secrets request in Claude Code asks for an encrypted secrets store for cloud sessions. 174 upvotes. Source: [anthropics/claude-code#32733](https://github.com/anthropics/claude-code/issues/32733).
- HN users describe the same design: store keys in a keyring, and let a tool fill them in when the command runs ([10keane, jvqv](https://news.ycombinator.com/item?id=47736831)). Others keep keys in a cloud secret manager the agent cannot reach ([kageiit](https://news.ycombinator.com/item?id=46825555)).
- Five or more "vault for agents" tools launched on HN in 2026: [Agent Vault](https://news.ycombinator.com/item?id=47865822), [OneCLI](https://news.ycombinator.com/item?id=49023427), [AgentSecrets](https://news.ycombinator.com/item?id=47167691), [Vultrino](https://news.ycombinator.com/item?id=49167812), [AgentKey](https://news.ycombinator.com/item?id=47822058). A commenter on Agent Vault said their team was frustrated that no such tool existed ([wfinigan](https://news.ycombinator.com/item?id=47865822)).

### 5. Drop cause: setup is too hard

- The Agent Vault team said on HN that their first design is clunky, mostly in configuration setup. Source: [dangtony98 on HN](https://news.ycombinator.com/item?id=47865822).
- The `op run -- claude` wrapper works, but the user must remember to always start the harness through it. Source: [anthropics/claude-code#23642](https://github.com/anthropics/claude-code/issues/23642).
- Non-engineers paste keys into chat, because secret tools need CLI setup they cannot do. Source: [anthropics/claude-code#29910](https://github.com/anthropics/claude-code/issues/29910).
- CmdWarden itself: a clean Windows install took 6 to 10 minutes and 10 steps to the first block. The .NET 10 SDK (215 MB, one UAC prompt) and manual enroll steps caused most of it. Nothing showed the user that the gate works. Source: [#59](https://github.com/BasantPandey/CmdWarden/issues/59).

### 6. Drop cause: the guard breaks normal work

- A `Read(**/.env)` deny rule made every `rg` search ask for approval, in every repo, even with no `.env` present. Source: [anthropics/claude-code#91681](https://github.com/anthropics/claude-code/issues/91681).
- In #401, users changed their install method to escape the `.env` load that broke their tests. Source: [anthropics/claude-code#401](https://github.com/anthropics/claude-code/issues/401).
- Agent Vault users report broken tools and too-wide capture: [#132](https://github.com/Infisical/agent-vault/issues/132) (breaks Tailscale), [#194](https://github.com/Infisical/agent-vault/issues/194) (Codex WebSocket fails), [#210](https://github.com/Infisical/agent-vault/issues/210) (proxies everything).

### 7. Trust: who guards the guard

Every agent-vault launch on HN gets the same questions.

- Can the agent reach the vault itself? Does the agent control the machine where the vault runs? Sources: [hebetude](https://news.ycombinator.com/item?id=47865822), [doctorpangloss](https://news.ycombinator.com/item?id=49023427).
- Does the tool only move the trust to a new key? Sources: [adithyassekhar](https://news.ycombinator.com/item?id=49023427), [Unsponsoredio](https://news.ycombinator.com/item?id=47865822).
- Does data leave the machine? One user told a team to treat all secrets as compromised after a skill sent every bash command to a server. Source: [TheTaytay](https://news.ycombinator.com/item?id=47707480).
- On Windows, an unsigned installer shows "unknown publisher" in SmartScreen. Many projects report that users read this as unsafe, and work PCs block it. Example: [open-webui/desktop#117](https://github.com/open-webui/desktop/issues/117). For a security tool, this hurts more.

### 8. Fear: the agent does damage with real credentials

- Anthropic lists real cases: an agent grepped env vars and config files for other tokens after an auth error. An agent uploaded a GitHub token to a compute cluster. An agent deleted remote branches and tried a migration on a production database. Source: [Anthropic engineering](https://www.anthropic.com/engineering/claude-code-auto-mode).
- The `.env` load in #401 wiped local databases during tests. Source: [anthropics/claude-code#401](https://github.com/anthropics/claude-code/issues/401).

### 9. Smaller asks

- Redact secret values in tool output. Source: [anthropics/claude-code#20966](https://github.com/anthropics/claude-code/issues/20966).
- Read secrets from 1Password with `op://` references. 25 upvotes. Source: [anthropics/claude-code#23642](https://github.com/anthropics/claude-code/issues/23642).
- Use an existing secrets manager as the store. Source: [Infisical/agent-vault#124](https://github.com/Infisical/agent-vault/issues/124).
- Approve a single API call by hand. Source: [Infisical/agent-vault#192](https://github.com/Infisical/agent-vault/issues/192).

## What makes someone install in one minute

We found no source that measures this directly. These points come from the praise, the asks, and the drop causes above.

1. A fresh scare. People act after an incident they can name: s1ngularity, Shai-Hulud, the `.env` read. Launch posts that name the incident get the "we needed this" replies.
2. No new habit. The tool keeps `gh`, `git`, and `npm` as they are. No wrapper command to remember (see #23642).
3. One command, then proof. The user sees the tool stop a real request before they lose interest.
4. Visible trust. Signed files, local only, open source, and a clear answer to "can the agent turn it off?"

## Candidate features and adoption fixes for CmdWarden v0.8.0

Ranked by the value test: a first real block in under 2 minutes, and CmdWarden still on after one week.

1. **One-step setup that ends in a real block.** One `cw` command enrolls the terminal and the harness, hardens `gh` and `git`, fixes PATH, and then shows the Approval Gate for a safe sample request. Evidence: findings 5 and 7, #59 (10 steps, no first-block moment). Feeds [#61](https://github.com/BasantPandey/CmdWarden/issues/61).
2. **Install with no .NET SDK, from a signed release.** Ship a self-contained, signed build, and add a winget package. Evidence: #59 (SDK is the top friction, v0.6.0 unsigned), finding 7 (SmartScreen). Feeds [#60](https://github.com/BasantPandey/CmdWarden/issues/60).
3. **A prompt budget.** Ask only for risky classes by default. Make "approve for this task" (timed grant) the default button. Never ask for the user's own terminal. Show the prompt count in the tray. Evidence: finding 2 (93% approved, rubber-stamp within a week, 1Password prompt storms).
4. **Guard project `.env` files.** Add a command that moves `.env` values into the Vault and releases them only to the child process of a gated run. Make the Claude Code and Cursor hook block direct reads of `.env` (`Read`, `type`, `cat`, `printenv`). Evidence: finding 1, the top theme. Extends the MCP config work in #28.
5. **Lead with the supply-chain story.** Show that an npm `postinstall` script (an unenrolled launcher) cannot get the `gh` or `npm` token. Put this demo on the docs home page. Evidence: finding 3 (s1ngularity, Shai-Hulud).
6. **A trust page and self-protection.** One page: what leaves the machine (nothing), how to check signatures, and proof that the harness cannot run `cw unharden` or change policy without the Approval Gate. Evidence: finding 7.
7. **A weekly summary.** Show "this week: N blocks, N approvals, N prompts" in the Vault app or tray. This shows value and keeps the prompt count in view. Evidence: inference from findings 2 and 3. No source asks for it directly.
8. **Later: 1Password as a backing store.** Real ask (25 upvotes), but it adds setup for the solo user. Evidence: finding 9.
