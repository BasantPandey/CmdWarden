# CLI TUI plan

Goal: make `cw` output look modern. Keep it fast and safe.

## Decision

Use **Spectre.Console 0.57.2** in `CmdWarden.Cli` only.

Why Spectre.Console:

- It is the standard .NET rich console library. Tables, trees, panels, colors, spinners, secret prompts.
- It falls back to plain text when output is redirected or `NO_COLOR` is set. Tests keep their plain-text asserts.
- Windows 10 conhost gets ANSI colors. It also works in Windows Terminal.
- One package, no native code, no runtime install.

Rejected:

- **Spectre.Console.Cli** (arg parser). The hand-rolled switch in `Program.cs` works. A rewrite changes every command. Add it only when we add many new commands.
- **Terminal.Gui 2.5** (full-screen app). `cw` is a one-shot CLI. The WPF windows (Approval Gate, Vault) already cover interactive UI.

Do not touch the shims, credential helpers, or the Agent. They must stay small and fast.

## Rules

1. Wrap every dynamic string with `Markup.Escape()`. Paths and names can contain `[` and `]`.
2. Never print a secret value. Same rule as today.
3. Keep the exact text of lines the process tests assert on. Grep `Assert.Contains` in `tests/` before you change a line.
4. Keep exit codes the same.
5. One glyph set: `[green]OK[/]`, `[yellow]WARN[/]`, `[red]FAIL[/]`. No emoji. Legacy conhost fonts break on emoji.

## Status

Steps 1 to 7 are done (PR feat/cli-spectre-tui).

## Steps

### 1. Add the package and a small `Ui` helper

- `dotnet add src/CmdWarden.Cli package Spectre.Console --version 0.57.2`
- New file `src/CmdWarden.Cli/Ui.cs` with one-line helpers: `E`, `Ok`, `Warn`, `Fail`, `Dim`, `Title`, `Line`, `Kv`, `Table`, `Status`, `StatusAsync`.
- `Status` runs the work without a spinner when output is redirected. This keeps redirected output clean.
- `Console.OutputEncoding = UTF8` at startup so box lines show in Git Bash and Windows Terminal.
- Check: `dotnet build` and `dotnet test --filter Category!=Process` pass.

### 2. `cw doctor`

Highest visibility command. Do it first.

- Header: product name and version in a `Rule`.
- Checks as a `Table` with columns: check, state, detail. Rows: pipe, product root, agent binary, vault UI, shortcut, session agent, PATH order.
- State cell uses the glyph set. Detail cell escapes paths.
- `hardened tools` block becomes a second table: tool, state, note.
- Check: `cw doctor` in Windows Terminal and in plain `cmd.exe`. Also `cw doctor > out.txt` shows no escape codes.

### 3. Lists as tables

- `harden --list` (shares `PrintHardenedTools` with doctor).
- `policy list`: launchers table with kind, key, per-tool level.
- `policy sessions`: colored lines, not a table. A table wraps the `gh / ...` cell at 80 columns and breaks the test assert.
- `audit`: table with time, tool, launcher, class, decision. `AuditFormatter.FormatLine` stays for `--json` or redirected use.
- `scan`: one `Panel` per finding, severity colored. `ScanFormatter.Format` stays for redirected output.
- `agent status`: two-line `Kv` output with colored UP or DOWN.

### 4. Progress on slow commands

- `harden gh|git|az|docker`: `AnsiConsole.Status().Start("Pinning gh...")` around discover, pin, shim copy, import.
- `agent start`: spinner while the lazy start polls the pipe.
- Skip if the redirected path runs. Spectre does this on its own.

### 5. Secret prompt

- `save`: replace `ReadSecretLine()` with `new TextPrompt<string>("Enter value for secret 'X':").Secret()`.
- Keep the stdin path for scripts.
- Delete `ReadSecretLine` when nothing else uses it.

### 6. Help

- `PrintHelp`: command names in `[bold]`, groups by area with a `Rule` each. Same words as today.
- `Unknown`: red first line, then help.

### 7. Verify

- Run the release smoke steps from `docs/install.md` on the packed tool.
- Screenshot `cw doctor`, `cw harden --list`, `cw scan` for the README.
- Run the full test suite including `Category=Process`.

## Out of scope

- Live dashboard or full-screen mode. Add Terminal.Gui only if a user asks for a watch mode.
- Arg parser rewrite. Add Spectre.Console.Cli when the switch in `Program.cs` gets a third nesting level.
- Themes or config for colors. `NO_COLOR` is enough.
