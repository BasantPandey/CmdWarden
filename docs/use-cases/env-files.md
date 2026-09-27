---
title: Keep .env secrets out of the agent
description: Move the secret values of a .env file into the CmdWarden vault. Your dev server still gets them; the AI agent sees only cw://NAME and [CmdWarden: NAME].
---

# UC16 Keep .env secrets out of the agent

**When:** Your project has a `.env` file with API keys, database passwords, or tokens. An AI agent works in the same folder. You want your program to get the values, and the agent never to see them.

The ignore rules of Claude Code and Cursor hide `.env` from their file tools. They do not stop a shell command such as `cat .env`. CmdWarden takes the values out of the file.

## 1. Move the values into the vault

```powershell
cd C:\src\my-app
cw env import
```

*Expect:*

```text
── CmdWarden env import .env ───────────────────────────────
  STRIPE_SECRET_KEY: -> vault STRIPE_SECRET_KEY
  DATABASE_URL: -> vault DATABASE_URL
Move these values into the vault and rewrite the file? [Y/n]
  Moved. 2 value(s) are in the vault; .env holds no secret value now.
  Run: cw inject --env-file .env -- <command>
```

The file keeps its shape. Each moved line now names a vault entry:

```text
PORT=3000
STRIPE_SECRET_KEY=cw://STRIPE_SECRET_KEY
DATABASE_URL=cw://DATABASE_URL
```

`cw env import` moves the names that look secret: `*KEY*`, `*SECRET*`, `*TOKEN*`, `*PASSWORD*`, `*CREDENTIAL*`, `*AUTH*`, `*PRIVATE*`, `*DSN*`, `*CONNECTION*`, and `DATABASE_URL`. `--all` moves every value. Comments, empty lines, and other settings stay.

Two projects with the same key name? Use a prefix: `cw env import --prefix myapp_` stores `myapp_STRIPE_SECRET_KEY`. When the vault already has a different value for a name, the import stops and changes nothing.

## 2. Run your program with the values

```powershell
cw inject --env-file .env -- npm run dev
```

One Approval Gate card covers every `cw://` name in the file. Your program gets the real values. The plain lines, such as `PORT=3000`, pass through as they are.

The output of the program shows `[CmdWarden: STRIPE_SECRET_KEY]` in place of a value. So an `echo` or a log line never hands the value to the agent. Need the raw output for one run? Add `--no-masking`.

## 3. What the agent sees

- `cat .env` shows only `cw://` names.
- A read of a `.env` file that still holds plain values is stopped by the CmdWarden hook, with a message that tells the agent to use `cw inject --env-file`. The hook covers Claude Code, Cursor, and Codex.
- `cw setup` also adds `Read(**/.env)` deny rules to Claude Code.

Undo: the values stay in the vault. Put them back by hand, or delete them with `cw delete <NAME>`.
