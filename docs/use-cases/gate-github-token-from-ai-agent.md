# Gate your GitHub token from an AI agent

**When:** You use Cursor / Claude Code / Codex and also use `gh`. You want the agent constrained, while your own terminal stays productive.

**Outcome:** Terminal can do normal `gh` work (Trusted default). AI harness defaults to **Read** (list/view style); writes and secret-reveal need a prompt or a higher policy level.

```powershell
# 1. Install the latest release (other ways: the Install page)
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Install-CmdWarden.ps1 -Desktop

# 2. Health
cw doctor

# 3. Enroll YOUR normal shell (Windows Terminal / PowerShell - not inside the agent)
cw whoami
cw policy enroll --kind terminal

# 4. Enroll the AI harness (run these from that harness integrated terminal)
cw whoami
cw policy enroll --kind ai-harness

# 5. Harden gh (pin + PATH shim + import token into Vault as GH_TOKEN)
cw harden gh

# 6. New shell so PATH sees the shim, then verify
where.exe gh
gh auth status
cw policy list
```

*Expect:* `cw doctor` shows Session Agent UP; enrollments appear in `policy list`; first `gh` on PATH is under CmdWarden shims; agent-driven `gh` that needs more than Read may prompt or block depending on command class.

**Why two enrollments?** Policy is **tool + launcher**. Same `gh`, different caller (you vs agent) can get different levels.
