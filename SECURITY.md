# Security policy

CmdWarden holds secrets and gates AI agents. We take each report seriously.

## Supported versions

Only the [latest release](https://github.com/BasantPandey/CmdWarden/releases/latest) gets security fixes. Run `cw update` to get it.

## Report a problem

Do not open a public issue for a security problem.

1. Open a [private security advisory](https://github.com/BasantPandey/CmdWarden/security/advisories/new).
2. Tell us the CmdWarden version (`cw version`), the Windows version, and the AI harness.
3. Give the steps that show the problem, and what an attacker gets.

We answer in 7 days. We tell you the plan for a fix, and we name you in the advisory if you want that.

## In scope

- A way to get a vault secret with no Approval Gate card, or past a Deny.
- A way for an AI harness to change the policy, the grants, or the vault, past the policy hook or the Session Agent checks.
- A secret value in the audit, in a log, in the agent output, or in a place that the docs say it never goes.
- The placeholder proxy: a value that goes to a host, a path, a field, or a header that its key does not name.
- A fake click or a fake key press that the Approval Gate card accepts.
- The installer, `cw update`, or a release file that runs code that is not from this repo.

## Out of scope

- An agent that runs as your own Windows user and edits the files of your user. [Trust](docs/trust.md#what-an-ai-agent-can-do-to-cmdwarden) explains this limit and what to do.
- A problem that needs admin rights on the PC first.
- Social engineering of the person who clicks the card.

## Check a release

- Compare the sha256 of a file with the [release page](https://github.com/BasantPandey/CmdWarden/releases). `cw update` does this for you.
- Check that a file came from this repo and its release workflow:

  ```powershell
  gh attestation verify CmdWarden.<version>-setup.zip --repo BasantPandey/CmdWarden
  ```
