# Undo or uninstall

**When:** You want to remove a launcher from policy, or remove CmdWarden from the PC.

## Remove one launcher from policy

```powershell
cw policy list
cw policy unenroll <policyKey>
```

*Expect:* unenroll removes the policy row of that launcher. The next command from it gets the default for an unknown launcher: deny.

## Remove CmdWarden

Open Windows **Settings > Apps**, select **CmdWarden**, and click **Uninstall**. Or double-click `uninstall.cmd`.

*Expect:* the uninstaller unhardens `gh`, `git`, and `docker`. Then it removes the tool, the shortcuts, the PATH entries, and `%LOCALAPPDATA%\CmdWarden\`. It asks before it deletes saved secrets. It works when `cw` is broken. See [Install, section 8](../install.md#8-uninstall) for the options.
