# Gate ssh key use

**When:** Your ssh keys sit in the OpenSSH agent, and an AI harness runs `git push` over ssh or `ssh` to a server. You want the harness to ask you before it signs with a key, and your own terminal to push with no popup.

```powershell
# once, as admin, if the OpenSSH agent does not run yet
Set-Service ssh-agent -StartupType Automatic; Start-Service ssh-agent
ssh-add

# then, as yourself
cw harden ssh
```

Open a new terminal and restart the AI harness, so they read the new `SSH_AUTH_SOCK`.

*Expect:* `cw harden ssh` sets your user `SSH_AUTH_SOCK` to the CmdWarden ssh-agent pipe (`//./pipe/CmdWarden-<you>-ssh`). The Session Agent serves that pipe. It asks the Approval Gate before each sign, then sends the request to the real agent (`\\.\pipe\openssh-ssh-agent`). A list of keys, `ssh-add`, and the other requests go to the real agent as they are.

If git has no `core.sshCommand`, harden sets it to `C:/Windows/System32/OpenSSH/ssh.exe`. The ssh of Git for Windows cannot read a Windows pipe; the Windows OpenSSH client can.

## The command class of a sign

The gate reads the command line of the ssh client:

| Client | Class |
|--------|-------|
| `ssh host git-upload-pack …` (git fetch, pull, clone) | read |
| `ssh host git-receive-pack …` (git push) | write |
| `ssh host` (a shell) or `ssh host <any command>` | write |
| `ssh-keygen -Y sign` (git commit signing) | write |
| any other program | unknown |

The launcher is the process above ssh, git, and the shell that git starts, with the same rules as for the shims. With the default levels, your terminal (Trusted) pushes with no popup. An AI harness (Read) can fetch, and a push shows the card:

- the impact line: `Signs in to git@github.com with the ssh key you@laptop (SHA256:…). Repo owner/repo.git.`
- the ssh command, and the key under **KEYS**.

Set a level for ssh only:

```powershell
cw policy set <policyKey> ssh Trusted
```

Each sign writes an audit row with tool `ssh`, purpose `sign`, and the key name. No key material leaves the real agent.

## Other agents

`--upstream` names another agent pipe, for example the 1Password or KeePassXC pipe:

```powershell
cw harden ssh --upstream \\.\pipe\openssh-ssh-agent
```

## Limits

- A process that sets `SSH_AUTH_SOCK` back to the real agent pipe, or reads a key file from `~/.ssh`, skips the gate. Keep your keys in the agent only, with a passphrase on the files.
- The gate reads the command line of the ssh client for the host. A client that is not `ssh.exe` gets class `unknown`, so it asks below the Full level.

## Undo

```powershell
cw unharden ssh
```

It puts `SSH_AUTH_SOCK` and `core.sshCommand` back and stops the gate.
