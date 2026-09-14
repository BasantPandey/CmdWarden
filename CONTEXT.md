# Domain glossary

Ubiquitous language for **CmdWarden**. No implementation detail here.

## Terms

### CmdWarden
The product: a Windows security layer that gates CLI secret use by **tool** and **Launcher** identity so AI harnesses and supply-chain code cannot freely read or exfiltrate credentials. Inspired twin of macOS Automic Vault’s *job*, not a brand or CLI clone.

CLI: primary command **`cw`**, alias **`cmdwarden`**.

### Session Agent
The long-lived **per-user** background process that holds policy, performs identity checks, brokers secrets from the vault, records audit events, and can show approval UI in the interactive logon session.

Not an AI coding agent.

### AI Harness
An application or CLI that runs model-driven tool use on the developer's machine (e.g. Cursor, Claude Code, Codex). Treated as a **Launcher** when it starts hardened tools.

### Launcher
The verified caller identity for a tool invocation — derived from the process chain (and signatures or path/hash). Policy is keyed by **tool + launcher**, not by tool alone.

### Vault
The product's secret store. On this effort, secrets are held in **Windows Credential Manager**, released only when policy allows.

### Shim
A small executable installed early on **PATH** that stands in for a hardened tool, asks the Session Agent for access, and only then invokes the real tool with secrets for that run.

### Harden
Opt-in per-tool setup that removes or avoids plaintext secret exposure for that tool and routes use through a **Shim** + **Session Agent** policy gate.

### Compat Mode
The default **Harden** shape. The **Shim** gates the tool run; the tool's own credential store stays in place.

### Strong Mode
Opt-in **Harden** shape per tool. Harden moves the tool's stock credentials into the **Vault** and deletes the originals. The tool then gets a credential only through a **Credential Helper** or a child env for one gated run. `cw unharden` writes the originals back.

### Credential Helper
A small executable that a tool itself calls to get, store, or erase a credential (the docker `credsStore` and git `credential.helper` protocols). In **Strong Mode** the helper asks the **Session Agent**, which checks that the pinned real tool sits above the helper in the process chain.

### Scan
Detection of tool configurations and habits that expose secrets or make exposure likely, with mitigation guidance (often leading to Harden).

### Approval Gate
Interactive human decision when policy does not auto-allow a secret use or side-effecting action. Delivered via native Windows UI in the user session.

### Policy
Rules that say, for a **tool + Launcher** pair and a **Command Class**, whether to auto-allow, prompt (Approval Gate), or block.

### Policy Level
Per tool × Launcher setting. CmdWarden v1 levels:

- **Deny** — never auto-allow; Approval Gate if available, else block
- **Read** — auto-allow **read** only
- **Trusted** — auto-allow **read** and **write**, not **secret-reveal**
- **Full** — auto-allow all classes including **secret-reveal**

### Command Class
Coarse classification of a tool invocation: **read**, **write**, **secret-reveal**, or **unknown**. **unknown** does not auto-allow except under **Full**.

### Spike Vertical
The first end-to-end proof path: Shim → Session Agent → identity → Vault → Approval Gate → real `gh`.

### First Catalog
The tools the implementation roadmap designs for scan + harden in depth: **`gh`**, **`git`**, **`az`**, **`docker`**. Only **`gh`** is required for the Spike Vertical in this map.

### Inspired Twin
A Windows product with the same *job* as Automic Vault on macOS, without requiring shared codebase, CLI parity, or upstream alignment.
