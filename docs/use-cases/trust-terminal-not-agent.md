# Trust your terminal fully, not the agent

**When:** You want maximum friction for the harness and less for yourself.

```powershell
# From terminal after enroll + harden:
cw whoami
cw policy set <terminalPolicyKey> gh Full

# From harness identity (or use key from when you enrolled the harness):
cw policy set <harnessPolicyKey> gh Read
# or stricter:
cw policy set <harnessPolicyKey> gh Deny
```

`cw policy list` shows the defaults per launcher kind and one row per enrolled launcher:

![cw policy list output: defaults line and a table of launcher keys, kinds, and levels](../images/cli-policy-list.svg)

*Expect:* terminal-key rows allow more; harness-key stays Read or Deny. Levels: **Deny**, **Read**, **Trusted**, **Full**.
