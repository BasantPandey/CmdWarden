# Audit and scan when something feels wrong

**When:** A command was blocked, you are not sure the shim is active, or you suspect leftover env tokens.

```powershell
cw doctor
cw whoami
cw policy list
cw scan
cw audit
cw audit -n 20
```

`cw scan` lists residual risks with the command that fixes each one:

![cw scan output: two info findings with tool, title, summary, evidence, and remediation](../images/cli-scan.svg)

`cw audit` lists the newest gate decisions. `TransientReuse` means a repeated command reused your earlier click:

![cw audit output: auto-allow and allow-once rows with launcher, tool, class, level, and secret name](../images/cli-audit.svg)

*Expect:* doctor = agent health; whoami = which launcher CmdWarden sees; scan = residual risks (e.g. ambient tokens, PATH issues) without printing secrets; audit = recent allow/deny decisions (including Approval Gate Deny / Approve Once).

**In CmdWarden Vault:** **Doctor** = `cw doctor`, **Hardened Tools** = `cw harden --list`, **Detectors** = `cw scan`, **Secret Usage** = `cw audit`, **Secret Gates** = `cw policy list` + `cw policy sessions`. The Detectors page scans the app's own environment. Run `cw scan` in a terminal to check that terminal's variables.
