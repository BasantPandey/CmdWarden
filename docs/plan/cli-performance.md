# CLI start time and memory

This page records the before check for `cw` start time and memory. [Issue 113](https://github.com/BasantPandey/CmdWarden/issues/113) holds the same record. Run the same check on the candidate build. Compare the two tables before you choose a Go rewrite.

## How to check

Use a Release build. Run each command 7 times. Drop the first run. Keep the median of the rest.

Record these values for each run:

- Wall time. This is how long the person waits.
- CPU time. This is the work the process does.
- Peak working set. This is the RAM the process touches.
- Private bytes. This is the memory the process asks for itself.
- Exit code.

Run the set with the Session Agent up. Run the set again with the Session Agent down.

Sample the working set while the process is alive. After exit, Windows often reports 0.

Commands for the table:

- `cw version`
- `cw` with no args
- `cw hook check grok` with stdin `{"tool_name":"run_terminal_command","tool_input":{"command":"echo hi"}}`
- `cw hook check grok` with stdin `{"tool_name":"run_terminal_command","tool_input":{"command":"git status"}}`
- `cw policy list`
- `cw scan`
- `cw doctor`
- `cw agent status`
- the gh shim with `--version`
- the git shim with `--version`
- real `git --version` as the floor

Leave these commands out of the set. They change the PC, or they wait on stdio: `setup`, `harden`, `save`, `read`, `delete`, `inject`, `launch`, `try`, `update`, `uninstall`, and `mcp`.

## Before

Date: 2026-10-26. Build: `dotnet build -c Release -p:CwSkipBundle=true`. The Session Agent was down. Exit code 2 means the agent did not answer.

The first `cw version` after the build took 1100 ms. Later warm runs took about 75 to 93 ms. The file cache makes a later cold run look fast.

| Command | Warm wall | CPU | Peak RAM | Private | Exit |
|---|---:|---:|---:|---:|---:|
| `cw version` | 75 to 93 ms | 55 ms | 19 MB | 6 MB | 0 |
| `cw` help | 185 to 232 ms | 172 to 219 ms | 32 MB | 14 MB | 0 |
| hook check, `echo hi` | 135 to 173 ms | 125 to 133 ms | 28 MB | 8 MB | 0 |
| hook check, `git status` | 5200 ms | 266 ms | 39 MB | 11 MB | 0 |
| `cw policy list` | 139 to 178 ms | 133 to 156 ms | 30 MB | 12 MB | 0 |
| `cw scan` | 179 to 234 ms | 141 to 172 ms | 34 MB | 12 MB | 0 |
| `cw audit -n 1` | 87 ms | 78 ms | not sampled | not sampled | 0 |
| `cw harden --list` | 202 ms | 172 ms | not sampled | not sampled | 0 |
| `cw agent status` | 2178 ms | 250 ms | not sampled | not sampled | 2 |
| `cw whoami` | 1745 ms | 266 ms | not sampled | not sampled | 2 |
| `cw doctor` | 3864 ms | 391 ms | not sampled | not sampled | 2 |
| gh shim `--version` | 4807 ms | 344 ms | 38 MB | 12 MB | 2 |
| git shim `--version` | 4805 ms | 383 ms | not sampled | not sampled | 2 |
| real `git --version` | 42 to 66 ms | 8 ms | 5 MB | 1.4 MB | 0 |

Help for most commands takes about 75 to 90 ms. `cw delete --help` and `cw inject --help` took about 1750 ms and exited 2. Those two paths talk to the agent before they accept `--help`.

The `cw` output folder is 4.6 MB. The gh shim folder is 2 MB. The shared .NET 10 runtime on disk is 76.5 MB for the core and 28.5 MB for ASP.NET. Each start maps that runtime into the process.

## What the numbers mean

`cw version` is close to a small Windows process once the files are in the cache. Help loads Spectre.Console. That adds about 100 ms and about 13 MB of private memory.

The 4.8 s shim time is mostly a wait. The CPU time is 344 ms. The code waits on the named pipe, then stops. `cw hook check` on `git status` waits on a 5 s policy timeout, then allows the command. A faster language still waits on that timeout. Shorten the down-agent fail path in its own change.

The language cost on the shim is the extra CPU time and the 38 MB working set. Real git uses about 5 MB and about 8 ms of CPU.

The agent-up column is empty. Fill that column before you choose a rewrite. With the agent up, the shim time is the start time, plus one pipe call, plus the real tool.

## Partial Go rewrite

Keep the Session Agent, the Approval Gate, the Vault, policy, harden, and setup in C#. Rewrite only the programs that start on every tool call.

In scope:

- The five PATH shims: gh, git, az, docker, and the pack shim. Those files are about 700 lines. They pull in Contracts, gRPC, and Protobuf.
- The two credential helpers for git and docker. Those files are about 240 lines.
- One Go client for `session_agent.proto` over the Windows named pipe, with the same owner check. The proto file is 234 lines.

Out of scope:

- The CLI is about 7,100 lines. The contracts are about 9,100 lines. The agent is about 4,000 lines.
- A full CLI rewrite is a new product. Plan on several months. The agent stays in C#, so that rewrite does not shrink the agent.

Effort for one person:

- Go pipe client and the proto: about 1 to 1.5 weeks.
- Five shims, stop text, exit codes, and the current process tests: about 1.5 to 2 weeks.
- Two helpers: about 1 week.
- Portable build and `cw harden` copy the Go exe: about 3 to 4 days.
- Run this table with the agent up and with the agent down: about 2 days.

That slice is about 4 to 6 weeks.

A Go program for `cw hook check` only is a second slice. Add it only if the agent-up row is still slow. That slice is about 1 more week. Leave the leak guard in C#. It needs the redactor and the vault names.

## Decision rule

Build the candidate. Run the same table. Compare warm wall time, CPU time, peak RAM, and private bytes. Do this with the agent up and with the agent down.

Treat a Go shim as worth the 4 to 6 weeks only if, with the agent up, it is close to real git on time and on RAM. The target to test is under 100 ms warm and under 15 MB peak RAM, before the real tool starts.

Also run a one-week Native AOT publish of the current shims. Put that table beside the Go table. Pick the smaller change that hits the target.

## After

Paste the same table here when the candidate build exists. Do not merge a rewrite without that after table.
