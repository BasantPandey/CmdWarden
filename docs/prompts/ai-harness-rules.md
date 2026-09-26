# AI harness rules for CmdWarden

For Windows developers who use AI harnesses (Cursor, Claude Code, Codex, and similar) with `gh`, `git`, `az`, and `docker` under CmdWarden.

Copy the block below into the rules file of your harness: `CLAUDE.md`, `.cursorrules`, `AGENTS.md`, or the system prompt. Enroll the harness first ([user guide, UC2 Part 2](../use-cases/approve-secret-read-mid-session.md#part-2-enroll-the-ai-harness-stricter-needs-click-to-release-secrets)).

````markdown
# CmdWarden rules

This machine runs CmdWarden. It gates secret use by tool and by caller.
A Session Agent holds policy. Shims on PATH stand in for `gh`, `git`, `az`, and `docker`.
Secrets live in Windows Credential Manager. A gated command shows an Approval Gate popup on the desktop.
CLI: `cw` (alias `cmdwarden`). Desktop app: CmdWarden Vault. You are an **AI harness** launcher with level **Read**.

## Use the tools through the shim

- Call `gh`, `git`, `az`, and `docker` by name on PATH.
- Do not call the real binary by absolute path.
- Do not set `PATH`, `GH_PATH`, or any `CW_*` variable to get around the shim.

## Never reveal a secret

- Do not run `gh auth token`, `gh auth git-credential`, or any `gh` command with `--show-token`.
- Do not run `git credential fill`, `git credential-*`, or `git -c credential.*`.
- Do not run `az account get-access-token`, `az ad sp create-for-rbac`, `az ad sp credential reset`, `az keyvault secret show`, or `az storage account keys list`.
- Do not print a secret variable, for example `echo %GH_TOKEN%`.
- Do not write a secret value to a file, an env var, a profile, or the chat. Secret names are fine.

## Need a secret in a child process

- Run `cw inject +NAME -- <command>`. Only the child gets the variable.
- The command after `--` runs directly. Use `cmd /c` when you need `%VAR%` expansion.
- Ask the user before you run `cw inject`. It can show the Approval Gate.

## The Approval Gate

- A gated command pauses. A Windows popup shows **Deny | Allow for session | Approve Once**.
- Tell the user to look at the desktop. Then wait.
- Do not retry the command in a loop.
- Do not click, script, or close the popup. A closed popup is a deny.
- If no popup shows, run the checks under "When something fails".

## Read the result

- Shim exit code `2`: Session Agent down. The line says `Session Agent not reachable`. Tell the user to run `cw agent start`.
- Shim exit code `3` and the line says `you denied this`: the user clicked Deny. Stop. Do not retry.
- Shim exit code `3` and the line says `Approval Gate timed out`: the gate timed out. Stop. Tell the user.
- Shim exit code `3` with another line: the line names `NotEnrolled`, `UnknownLauncher`, `PinMissing`, or `PinMismatch`. Stop. Report that line. Do not work around a deny.
- Shim exit code `4`: the real tool did not start.
- Any other exit code comes from the real tool.
- `git` in strong mode fails with an auth error and stderr `CmdWarden: git credential denied for <url> (<reason>)`. Treat it as a deny.

## cw commands you may run on your own

Read-only: `cw version`, `cw agent status`, `cw whoami`, `cw policy list`, `cw policy path`, `cw policy sessions`, `cw harden --list`, `cw audit -n 20`, `cw scan`, `cw shortcut status`, `cw doctor`, `cw update --check`.
Note: `cw doctor` starts the agent when it is down.

## cw commands that need the user's explicit ask

`cw save`, `cw delete`, `cw inject`, `cw policy enroll`, `cw policy set`, `cw policy unenroll`, `cw policy sessions --revoke`, `cw harden`, `cw unharden`, `cw agent start|stop`, `cw doctor --fix-path`, `cw shortcut install|remove`, `cw update`, `cw uninstall`.
- Never run `cw policy set <key> <tool> Full`.
- Never enroll yourself with `--kind terminal`. A harness is `--kind ai-harness`.

## Do not touch the plumbing

- Do not edit `policy.json`, Credential Manager entries, `%LOCALAPPDATA%\CmdWarden`, the shims folder, or PATH.
- Do not run `cw` or the tools from an elevated shell. An elevated agent is not reachable from normal windows.

## CmdWarden Vault

It is a desktop window. You cannot open it or click in it. Point the user to the page:
Secret Gates = `cw policy list`, Detectors = `cw scan`, Hardened Tools = `cw harden --list`, Secrets = `cw save` / `cw delete`, Secret Usage = `cw audit`, Doctor = `cw doctor`.

## When something fails

Run `cw doctor`, `cw whoami`, `cw policy list`, and `cw audit -n 20`. Paste the output for the user. Name the reason code you saw.
````
