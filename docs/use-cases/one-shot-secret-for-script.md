# One-shot secret for a script

**When:** You need a password/API key in **one** child process (deploy script, curl, custom tool) and do not want it in `.env` committed or permanently in the parent shell.  
Also see [UC2 Part 1](approve-secret-read-mid-session.md#part-1-beginning-set-up-and-create-the-secret-no-popup) for the same save/inject tour with expected screens, and [UC9](vault-add-remove-secrets.md) for the desktop way to save and delete.

```powershell
cw save MY_API_KEY
# paste when prompted (or: cw save MY_API_KEY --value "…")

cw inject +MY_API_KEY -- cmd /c my-tool.exe --use-env
cw delete MY_API_KEY
```

*Expect:* secret stored as Credential Manager target `CmdWarden/secret/MY_API_KEY`; inject puts it **only in the child** env; parent shell does not keep it. Inject is policy-gated (you need an enrolled launcher that allows it).

**Prefer [UC1](gate-github-token-from-ai-agent.md) for `gh`:** do not use inject as a substitute for `cw harden gh` when the goal is gated GitHub CLI.
