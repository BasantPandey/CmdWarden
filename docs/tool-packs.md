---
title: Tool packs
description: Gate a CLI tool with one JSON file. No C# code. The pack gives the command classes, the secrets, and the config files to scan.
---

# Tool packs

A tool pack describes one CLI tool in one JSON file. One generic shim reads the pack, so a new tool needs no new code.

CmdWarden has three built-in packs: [npm](use-cases/gate-npm.md), [aws](use-cases/gate-aws.md), and [kubectl](use-cases/gate-kubectl.md). The tools `gh`, `git`, `az`, and `docker` keep their own code, because they have strong mode and credential helpers.

## Use a pack

```powershell
cw harden npm          # find npm.cmd on PATH, pin it, install npm.exe (the pack shim)
cw harden --list       # pack tools show next to the built-in tools
cw unharden npm        # remove the pin and the shim
```

`cw harden --list` has a **source** column: `built-in`, `pack`, or `user pack`. A user pack that does not load shows a `pack not loaded` line with the reason.

## Write your own pack

Put a JSON file in `%LOCALAPPDATA%\CmdWarden\packs\`. The file name does not matter; the `tool` field names the tool. A user pack with the same `tool` as a built-in pack replaces it.

```json
{
  "tool": "terraform",
  "displayName": "Terraform",
  "binaries": ["terraform.exe"],
  "secretEnv": ["TF_TOKEN_app_terraform_io"],
  "flagsWithValue": ["-chdir", "-var-file"],
  "default": "unknown",
  "rules": [
    // First match wins. Put the narrow rules first.
    { "match": "output -json|-raw", "class": "secret-reveal" },
    { "match": "destroy", "class": "write", "risk": "high" },
    { "match": "apply -auto-approve", "class": "write", "risk": "high" },
    { "match": "apply|import|taint|untaint|state", "class": "write" },
    { "match": "plan|validate|fmt|show|output|providers|version|graph", "class": "read" }
  ],
  "secretFiles": [
    { "path": "~/AppData/Roaming/terraform.d/credentials.tfrc.json", "pattern": "\"token\"\\s*:", "title": "Plain Terraform Cloud token" }
  ],
  "samples": [
    { "argv": "plan", "class": "read" },
    { "argv": "destroy -target=aws_s3_bucket.logs", "class": "write" },
    { "argv": "output -raw db_password", "class": "secret-reveal" }
  ]
}
```

Then run `cw harden terraform`. The pack file may have `//` comments.

## Fields

| Field | What it does |
|-------|--------------|
| `tool` | The command name: lower case letters, digits, `.`, `-`, `_`. The shim is `<tool>.exe`. It cannot be `gh`, `git`, `az`, or `docker`. |
| `displayName` | The name in the Vault app and in messages. |
| `logo` | Optional `{ "path": "<SVG path data in a 24x24 box>", "color": "#RRGGBB" }` for the Vault app and the Approval Gate. |
| `binaries` | File names of the real tool, in order, for example `["npm.cmd", "npm.exe"]`. Each ends in `.exe`, `.cmd`, or `.bat`. |
| `secretEnv` | Vault names. On an allowed run, each name that is in the vault goes into the child env as a variable of the same name. Save a value with `cw save <NAME>`. |
| `flagsWithValue` | Flags that take the next word as their value, so that word is not a command word. `--flag=value` needs no entry. |
| `default` | The class of a command that no rule matches. The default is `unknown`. |
| `rules` | The class rules. The first rule that matches wins. |
| `secretFiles` | Config files that `cw scan` reads for a plain secret. |
| `samples` | Sample commands and their class. |

### Rules

A rule has `match`, `class`, and two optional fields.

- **`match`** is words and flags split by spaces.
    - A word matches the command words in order, from the first one. `*` and `?` are wildcards. `a|b` is either word.
    - A flag (a token that starts with `-`) must be somewhere in the command, alone or as `flag=value`. `-a|-b` is either flag.
    - Words after `--` belong to the child command and do not count.
- **`class`** is `read`, `write`, `secret-reveal`, or `unknown`. Policy decides from the class, as for the built-in tools.
- **`secrets`** (default `true`): `false` gives the run no secret. Use it for commands that start other code, for example `npm run` or `npm install` without `--ignore-scripts`.
- **`risk`** (default `normal`): `high` always shows the popup below the Full level, and no remembered answer covers it. `low` lets an enrolled launcher run the write when `cw policy low-risk allow` is on.

A command that ends in `--help` or `-h`, a command of only `--version`, `-v`, or `-V`, and a command whose first word is `help` are help. Help is `read` and gets no secret.

### Secret files

`path` starts with `~` for the user profile, or is relative to the working folder. `pattern` is a regular expression for one line. A matching line is a **high** finding. The finding names the file and the line number, never the value. `remediation` is optional.

`cw scan` also reports a pack secret that is set in the environment, and a tool on PATH that has no pin.

## Samples are the pack tests

A pack does not load when a sample gets a different class. So each `cw harden` and each Session Agent call checks the samples of the pack. The test suite checks the samples of every built-in pack. Add a sample for each rule you write.

## Limits

- Compat mode only. The tool's own config and credential store stay in place. An absolute path to the real tool skips the shim.
- A `.cmd` or `.bat` tool runs through `cmd.exe`. The shim puts each argument in quotes. It stops the run when an argument has a `"`, a line break, or a `%NAME%` pair, because `cmd.exe` would read it as code.
- The risk line on the popup is plain for pack tools: `Runs <command>. You cannot undo this.` for a high-risk rule.
