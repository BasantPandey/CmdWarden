# Sign git commits with a gated ssh key

**When:** You sign your git commits with an ssh key, so GitHub shows them as **Verified**. You want an AI harness to ask you before it signs a commit in your name. Your own terminal signs with no popup.

Git signs a commit with `ssh-keygen -Y sign`. After `cw harden ssh`, that sign goes through the CmdWarden ssh gate, like a push over ssh ([UC13](gate-ssh-keys.md)). The gate reads it as class `write`.

## Step 1: Gate the ssh agent

Do [UC13](gate-ssh-keys.md) first. Your key must be in the OpenSSH agent:

```powershell
cw harden ssh
```

Open a new terminal. Then list the keys through the gate:

```powershell
ssh-add -L
```

*Expect:* one line for each key, for example `ssh-ed25519 AAAAC3Nza... you@laptop`. Copy the line of the key that signs.

## Step 2: Tell git to sign with that key

```powershell
git config --global gpg.format ssh
git config --global gpg.ssh.program "C:/Windows/System32/OpenSSH/ssh-keygen.exe"
git config --global user.signingkey "key::ssh-ed25519 AAAAC3Nza... you@laptop"
git config --global commit.gpgsign true
```

Put the line from Step 1 after `key::`. With `key::`, git asks the agent to sign, and no key file is read.

`gpg.ssh.program` must be the Windows OpenSSH `ssh-keygen`. The `ssh-keygen` of Git for Windows cannot read the gate pipe.

## Step 3: Add the key to GitHub as a signing key

On GitHub, open **Settings**, then **SSH and GPG keys**, then **New SSH key**. Set **Key type** to **Signing Key**, and paste the line from Step 1. A key that you use to push must also be there as an **Authentication Key**.

## Step 4: Check a signed commit

Make a file that tells git which key is yours, so git can check the signature:

```powershell
"you@example.com $(ssh-add -L | Select-Object -First 1)" | Set-Content "$HOME\.ssh\allowed_signers"
git config --global gpg.ssh.allowedSignersFile "$HOME/.ssh/allowed_signers"
```

Use the e-mail address of your git commits. Then make a commit in a test repo:

```powershell
git commit --allow-empty -m "Test the signature"
git verify-commit HEAD
```

*Expect:* `Good "git" signature for you@example.com with ED25519 key SHA256:...`.

## Who gets the card

| Who commits | Default level | Result |
|---|---|---|
| Your terminal | Trusted | Git signs with no popup |
| An AI harness | Read | You get the card for each signed commit |

The card shows the `ssh-keygen -Y sign` command and the key under **KEYS**. **Allow for session** lets the harness sign more commits until the session ends. A **Deny** stops the commit. Git shows `agent refused operation` and `fatal: failed to write commit object`.

Each sign writes an audit row with tool `ssh`, purpose `sign`, and the key name. To set a level for ssh only, run `cw policy set <policyKey> ssh <level>`.

## Limits

- The limits of [UC13](gate-ssh-keys.md) apply. A key file with no passphrase in `~/.ssh` lets a program sign with no gate. Keep the key in the agent only.
- Git for Windows 2.34 or later has ssh signing. Run `git --version` to check.

## Undo

```powershell
git config --global --unset commit.gpgsign
cw unharden ssh
```
