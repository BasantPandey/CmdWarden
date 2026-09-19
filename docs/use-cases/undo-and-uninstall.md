# Undo or uninstall

**When:** You want to remove a launcher from policy or stop using the tool package.

```powershell
cw policy list
cw policy unenroll <policyKey>
dotnet tool uninstall -g CmdWarden
```

*Expect:* unenroll removes that launcher’s policy row. Uninstall removes the global tool; data under `%LOCALAPPDATA%\CmdWarden\` (policy, pins, shims, audit) may remain until you delete that folder yourself. Run `cw shortcut remove` first to remove the Start Menu entry.
