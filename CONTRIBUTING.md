# Contributing to CmdWarden

Thank you for your help. This page tells you how to report a problem, how to build and test CmdWarden, and what a pull request needs.

## Report a problem

- **A bug or an idea:** open an [issue](https://github.com/BasantPandey/CmdWarden/issues). Give the `cw version` output, the command, what you expected, and what you saw. Do not paste a secret value.
- **A security problem:** do not open a public issue. Follow the [security policy](SECURITY.md).

## Build and test

You need Windows 10 or 11 and the [.NET SDK 10](https://dotnet.microsoft.com/download).

```powershell
dotnet build CmdWarden.sln
dotnet test CmdWarden.sln --filter "Category!=Process"
dotnet test CmdWarden.sln --filter "Category=Process"
```

- The first test run is fast. It runs the unit tests.
- The second run starts real Session Agents, shims, git, and ssh. It takes some minutes.
- Set `CW_FUZZ_ITERATIONS` to 100000 to run the fuzz tests longer. Set `CW_FUZZ_SEED` for other inputs.
- Build the portable folder with `pwsh ./scripts/Build-Portable.ps1 -Out artifacts/bin`.

A warning fails the build. Fix the warning; do not hide it.

## Test policy

- A new feature comes with tests that check it.
- A bug fix starts with a test that fails before the fix and passes after it.
- A change to a parser that reads data from an agent also gets seeds in `FuzzTests`.
- A new way around the gate, or a new answer to one, goes in the [threat model](docs/threat-model.md), with its test.

## Pull requests

- Open a pull request against `main`. A direct push to `main` is blocked.
- All checks must pass: the build, the unit tests, the process tests, CodeQL, and the dependency review.
- Keep one topic in one pull request. Name the issue, for example `(#97)`.
- Write commit messages as `type(area): what changed`, for example `fix(proxy): refuse a request with two framings`.
- Update the docs in the same pull request when a command or a screen changes.

## Writing style

The docs, the CLI text, and the code comments use [ASD-STE100 Simplified Technical English](https://www.asd-ste100.org/):

- One idea in one sentence, 20 words or less.
- The active voice and the present tense.
- Simple, common words. The same word for the same thing.
- An instruction starts with the verb.

## License

CmdWarden is under the [MIT license](LICENSE). A contribution that you send is under the same license.
