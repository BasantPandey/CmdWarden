# Passing evidence

This page records the Passing answers for project 16697.
The badge page is the [OpenSSF Passing page for project 16697](https://www.bestpractices.dev/en/projects/16697/passing).
This page does not change the vault or TOTP.

The tracks in issue #193 can run at the same time.
A blocked issue waits. The list below is the same order.

## Docs and repo

These three can start now.

`contribution_requirements` is Met.
The contribution rules are in `CONTRIBUTING.md`.
[CONTRIBUTING.md](https://github.com/BasantPandey/CmdWarden/blob/main/CONTRIBUTING.md)

`documentation_interface` is Met.
The external interface is the CLI reference.
[CLI reference](https://github.com/BasantPandey/CmdWarden/blob/main/docs/cli-reference.md)

`repo_interim` is Met.
The public repo has commits between release tags.
[Commits on main](https://github.com/BasantPandey/CmdWarden/commits/main)

## Versions

These three can start now.

`version_unique` is Met.
Each release tag is unique.
[Release tags](https://github.com/BasantPandey/CmdWarden/tags)

`version_semver` is Met.
Tags use MAJOR.MINOR.PATCH.
[Release v0.12.0](https://github.com/BasantPandey/CmdWarden/releases/tag/v0.12.0)

`version_tags` is Met.
Git tags name the releases.
[Release tags](https://github.com/BasantPandey/CmdWarden/tags)

## Release notes

Do `release_notes` first.
`release_notes_vulns` waits for it.

`release_notes` is Met.
The human summary is `NEWS.md`.
[NEWS.md](https://github.com/BasantPandey/CmdWarden/blob/main/NEWS.md)

`release_notes_vulns` is Met.
The 0.9.0 notes name GHSA-846f-f764-76mx.
Other releases in `NEWS.md` fix no public vulnerability.

## Bug reports

Do `report_tracker` first.
The next three wait for it. They can run together.

`report_tracker` is Met.
Bugs and ideas use GitHub issues.
[CmdWarden issues](https://github.com/BasantPandey/CmdWarden/issues)

`report_responses` is Met.
You get an answer to a bug report on that tracker.
The rule is in `CONTRIBUTING.md`.

`enhancement_responses` is Met.
You get an answer to an enhancement request on that tracker.

`report_archive` is Met.
Closed GitHub issues stay public.
[CmdWarden issue archive](https://github.com/BasantPandey/CmdWarden/issues?q=is%3Aissue)

## Vulnerability fixes

These three can start now.

`vulnerability_report_response` is Met.
You get an answer to a private report in 7 days.
[SECURITY.md](https://github.com/BasantPandey/CmdWarden/blob/main/SECURITY.md)

`vulnerabilities_fixed_60_days` is Met.
A public vulnerability is fixed in 60 days.
No open GitHub security advisory exists.
The 0.9.0 notes name GHSA-846f-f764-76mx.

`vulnerabilities_critical_fixed` is Met.
No open critical advisory exists.
A critical fix is faster than 60 days.

## Build tools

These two can start now.

`build_common_tools` is Met.
The build uses the .NET SDK.
[Build workflow](https://github.com/BasantPandey/CmdWarden/blob/main/.github/workflows/build.yml)

`build_floss_tools` is Met.
The .NET SDK is open source.
[.NET SDK repository](https://github.com/dotnet/sdk)

## Test run

Do `test_invocation` first.
The next two wait for it. They can run together.

`test_invocation` is Met.
`CONTRIBUTING.md` shows `dotnet test`.
[CONTRIBUTING.md](https://github.com/BasantPandey/CmdWarden/blob/main/CONTRIBUTING.md)

`test_most` is Unmet.
This page does not claim a coverage percent.

`test_continuous_integration` is Met.
The build workflow runs the tests on each pull request.
[Build workflow](https://github.com/BasantPandey/CmdWarden/blob/main/.github/workflows/build.yml)

## Test policy

Do these in order.

`test_policy` is Met.
A new feature comes with tests.
The rule is in `CONTRIBUTING.md`.

`tests_are_added` is Met.
The same rule requires a test for a bug fix.

`tests_documented_added` is Met.
`CONTRIBUTING.md` documents that rule.

## Warnings

Do these in order.

`warnings` is Met.
A compiler warning fails the build.
[Directory.Build.props](https://github.com/BasantPandey/CmdWarden/blob/main/Directory.Build.props)

`warnings_fixed` is Met.
`TreatWarningsAsErrors` is true.
A warning cannot stay in the build.

`warnings_strict` is Met.
The same switch is the strict warning rule.

## Secure knowledge

These two can start now.

`know_secure_design` is Met.
The threat model records the secure design.
[Threat model](https://github.com/BasantPandey/CmdWarden/blob/main/docs/threat-model.md)

`know_common_errors` is Met.
CodeQL checks the C# code for common errors.
[CodeQL workflow](https://github.com/BasantPandey/CmdWarden/blob/main/.github/workflows/codeql.yml)

## Crypto

These three can start now.
This change does not edit the vault store.
This change does not edit the TOTP code.

`crypto_weaknesses` is Unmet.
TOTP accepts SHA-1 for compatibility.
[OneTimeCode.cs](https://github.com/BasantPandey/CmdWarden/blob/main/src/CmdWarden.Contracts/Proxy/OneTimeCode.cs)

`crypto_pfs` is Met.
Downloads and the docs site use HTTPS.
TLS 1.2 and TLS 1.3 provide perfect forward secrecy.
[CmdWarden releases](https://github.com/BasantPandey/CmdWarden/releases)

`crypto_password_storage` is N/A.
The vault stores recoverable secrets with DPAPI.
It does not store authentication passwords.
[AzStrongStore.cs](https://github.com/BasantPandey/CmdWarden/blob/main/src/CmdWarden.Contracts/AzStrongStore.cs)

## Leaked credentials

This issue can start now.

`no_leaked_credentials` is Met.
Do not commit a real credential.
GitHub secret scanning watches this public repo.
[GitHub secret scanning](https://github.com/BasantPandey/CmdWarden/security)

## Static analysis

These three can start now.

`static_analysis_common_vulnerabilities` is Met.
CodeQL analyzes C# and the workflows.
[CodeQL workflow](https://github.com/BasantPandey/CmdWarden/blob/main/.github/workflows/codeql.yml)

`static_analysis_fixed` is Met.
CodeQL has no open finding in product code.
The open alerts are Scorecard checks, not code defects.

`static_analysis_often` is Met.
CodeQL runs on each push and each Monday.

## Dynamic analysis

`dynamic_analysis`, `dynamic_analysis_unsafe`, and `dynamic_analysis_enable_assertions` can start now.
`dynamic_analysis_fixed` waits for `dynamic_analysis`.

`dynamic_analysis` is Met.
The fuzz workflow runs the parsers each week.
[Fuzz workflow](https://github.com/BasantPandey/CmdWarden/blob/main/.github/workflows/fuzz.yml)

`dynamic_analysis_unsafe` is N/A.
The product code is C#.
It does not need a memory-unsafe sanitizer.

`dynamic_analysis_enable_assertions` is Unmet.
The product source has no `Debug.Assert` calls.
This change does not add them.

`dynamic_analysis_fixed` is Met.
The fuzz workflow fails when a parser throws.
