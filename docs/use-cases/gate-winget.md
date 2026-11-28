---
title: Gate winget
description: Let winget update and search run, and ask before install or uninstall.
---

# Gate winget

When: An AI harness runs `winget`, and you want an update to run, but an install to ask you.

```powershell
cw harden winget
```

Open a new terminal after harden.

*Expect:* `winget` through PATH starts the shim.
A read runs with no card when the harness is at Read.
An install, uninstall, or source change shows the card.

| Command | Class |
|---------|-------|
| `winget upgrade`, `winget update`, `winget list`, `winget search` | read |
| `winget source update`, `winget source list`, `winget source export` | read |
| `winget settings export` | read |
| `winget install`, `winget uninstall`, `winget repair` | write |
| `winget source add`, `winget source remove`, `winget source reset` | write |
| `winget pin`, `winget download`, `winget import`, `winget configure` | write |

`winget install -v 1.0` is still an install.
The `-v` is the version of the package, not a help flag.

## Undo

```powershell
cw unharden winget
```

See [Tool packs](../tool-packs.md) for the pack format.
