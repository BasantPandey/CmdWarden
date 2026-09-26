# Short-lived GitHub tokens for one repo

**When:** An AI harness runs `gh pr create` and `gh issue list` in your repos. A leak of your personal token works on every repo you own, for months. You want each gh run to get a token that works on the current repo only and ends after one hour.

```powershell
cw github app setup      # register a GitHub App in your browser; the key goes to the vault
# install the app on the repos you want (setup opens the page)
cw github app status     # in a repo folder: shows that a token for this repo works
```

*Expect:* `cw github app setup` opens a local page that sends an app manifest to GitHub. You confirm the new app on GitHub, and GitHub gives the app and its private key back once. CmdWarden keeps the key in the vault. Then install the app on your repos from the page that opens.

After an allowed gh run on one github.com repo, the child env has `GH_TOKEN` set to an installation token of the app:

- It works on that one repo only. The token request names the repo.
- It ends after one hour. The Session Agent keeps it until five minutes before the end, so most runs need no call to GitHub.
- It has the app permissions: contents, pull requests, issues, and actions (write), and metadata (read). It cannot change settings, secrets, or other repos.

The repo comes from `-R` / `--repo`, then `GH_REPO`, then the remote of the working folder. The commands are `gh pr`, `issue`, `run`, `workflow`, `release`, `label`, `cache`, `browse`, and `gh repo view` of the current repo.

Your personal token serves when:

- the command is not on one repo, for example `gh api user`, `gh repo create`, or `gh auth status`,
- the repo is not on github.com,
- the app is not installed on the repo, or GitHub does not answer.

Then the shim prints why, for example `CmdWarden: no GitHub App token for owner/repo (the GitHub App is not installed on owner/repo); gh uses your personal token.` The audit shows the row `GitHub App token for owner/repo`, or an `AppTokenFallback` row for `GH_TOKEN`.

## Use an app you made

```powershell
cw github app setup --app-id 123456 --key C:\path\app.private-key.pem
```

CmdWarden checks the key with GitHub, stores it in the vault, and you can delete the `.pem` file. For an organization app, use `cw github app setup --org <org>`.

## Undo

```powershell
cw github app remove
```

It deletes the key from the vault. gh then gets your personal token again. Delete the app on GitHub if you do not need it.
