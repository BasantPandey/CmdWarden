# Review a script that publishes

Use this when one script publishes a release and you do not want a card on every run.
You review that file in your own terminal.
A later run of that exact file may use the caps with no card.
The same command from an agent still shows the card.

```powershell
cw script bless C:\src\release.ps1 --cap gh=write
```

Use the full path. A relative path is stored against the folder you are in now.

*Expect:* the next run of `C:\src\release.ps1` can run `gh release create` with no card.
A change of one byte clears the review. The next run shows the card.
`gh auth token` still shows the card. A write cap does not cover a secret reveal.
Each run reads the file again. The review is not a session grant.

```powershell
cw script list
cw script drop C:\src\release.ps1
```

An agent cannot run `cw script bless` or `cw script drop`.
The hook tells the agent to ask you. You run those commands in your own terminal.
A read cap uses `--cap aws=read`. You can pass `--cap` more than once.
