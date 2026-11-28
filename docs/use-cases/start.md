---
title: Scan, harden, and check
description: Run doctor, scan this PC, and harden gh from one short guide.
---

# Scan, harden, and check

Use this when CmdWarden is installed and you want the first checks in one place.
Open your own terminal.
Run doctor first, so the agent is up before a card opens.

```powershell
cw doctor
cw scan
cw harden gh
```

Open a new terminal after `cw harden gh`.
The new terminal sees the gh shim.

*Expect:* `cw doctor` reports the agent.
`cw scan` lists plain secrets it can see.
`cw harden gh` pins gh.

An agent at Read runs a read command with no card.
An agent write shows the card in its own window.
Windows Hello confirms **Allow** when the caller is an agent.
A write from your own terminal does not ask for Hello.

```powershell
cw policy hello off
```

That command turns the agent Hello check off.
Then read [Let the agent read GitHub, gate writes and token export](agent-reads-github-writes-need-approval.md).
