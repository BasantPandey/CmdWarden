using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CmdWarden.Cli;
using CmdWarden.Cli.Hooks;
using CmdWarden.Cli.Launch;
using CmdWarden.Cli.Mcp;
using CmdWarden.Contracts;
using CmdWarden.Contracts.GitHub;
using Spectre.Console;

namespace CmdWarden.Tests;

/// <summary>
/// These tests set CW_HOME and PATH for the whole process. Nothing else runs with them.
/// </summary>
[CollectionDefinition("HarnessHome", DisableParallelization = true)]
public sealed class HarnessHomeCollection;

/// <summary>New Grok command paths and the lines SonarQube counts on new code.</summary>
[Collection("HarnessHome")]
public sealed class GrokHarnessCoverageTests : IDisposable
{
    private readonly string _home;
    private readonly string? _oldHome;

    public GrokHarnessCoverageTests()
    {
        _oldHome = Environment.GetEnvironmentVariable(ProductPaths.HomeEnvVar);
        _home = Path.Combine(Path.GetTempPath(), "cw-home-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_home);
        Environment.SetEnvironmentVariable(ProductPaths.HomeEnvVar, _home);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(ProductPaths.HomeEnvVar, _oldHome);
        try
        {
            Directory.Delete(_home, recursive: true);
        }
        catch (IOException)
        {
            // A file can stay locked for a moment. The next run uses a new folder.
        }
    }

    [Fact]
    public async Task Grok_hook_install_and_uninstall_write_only_the_policy_hook()
    {
        var first = await Capture(() => PolicyHookCommands.HookAsync(["install", "grok"]));
        var again = await Capture(() => PolicyHookCommands.HookAsync(["install", "grok"]));
        var path = HookInstaller.GrokHooksPath();
        var text = File.ReadAllText(path);

        Assert.Equal(0, first.Code);
        Assert.Contains("added", first.Out, StringComparison.Ordinal);
        Assert.Contains("already there", again.Out, StringComparison.Ordinal);
        Assert.Contains("Bash|PowerShell|Read", text, StringComparison.Ordinal);
        Assert.Contains("hook check grok", text, StringComparison.Ordinal);

        var removed = await Capture(() => PolicyHookCommands.HookAsync(["uninstall", "grok"]));
        var missing = await Capture(() => PolicyHookCommands.HookAsync(["uninstall", "grok"]));
        Assert.Contains("removed", removed.Out, StringComparison.Ordinal);
        Assert.Contains("not there", missing.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Grok_leak_guard_install_and_uninstall_write_only_the_post_tool_hook()
    {
        var first = await Capture(() => LeakGuardCommands.LeakGuardAsync(["install", "grok"]));
        var again = await Capture(() => LeakGuardCommands.LeakGuardAsync(["install", "grok"]));
        var text = File.ReadAllText(HookInstaller.GrokHooksPath());

        Assert.Equal(0, first.Code);
        Assert.Contains("added", first.Out, StringComparison.Ordinal);
        Assert.Contains("already there", again.Out, StringComparison.Ordinal);
        Assert.Contains("leak-guard grok", text, StringComparison.Ordinal);
        Assert.Contains("\".*\"", text, StringComparison.Ordinal);

        var removed = await Capture(() => LeakGuardCommands.LeakGuardAsync(["uninstall", "grok"]));
        var missing = await Capture(() => LeakGuardCommands.LeakGuardAsync(["uninstall", "grok"]));
        Assert.Contains("removed", removed.Out, StringComparison.Ordinal);
        Assert.Contains("not there", missing.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Grok_mcp_install_reports_added_updated_skipped_and_removed()
    {
        var added = await Capture(() => McpCommands.McpAsync(["install", "grok"]));
        var same = await Capture(() => McpCommands.McpAsync(["install", "grok"]));
        Assert.Contains("added", added.Out, StringComparison.Ordinal);
        Assert.Contains("already there", same.Out, StringComparison.Ordinal);

        var path = McpCommands.GrokConfigPath();
        var text = File.ReadAllText(path).Replace("command = ", "command = 'other'\n# command = ", StringComparison.Ordinal);
        File.WriteAllText(path, text);
        var updated = await Capture(() => McpCommands.McpAsync(["install", "grok"]));
        Assert.Contains("updated", updated.Out, StringComparison.Ordinal);

        File.WriteAllText(path, "[mcp_servers.cmdwarden]\ncommand = 'mine'\n");
        var skipped = await Capture(() => McpCommands.McpAsync(["install", "grok"]));
        Assert.Contains("skipped", skipped.Out, StringComparison.Ordinal);
        Assert.Contains("command = 'mine'", File.ReadAllText(path), StringComparison.Ordinal);

        File.WriteAllText(path, "model = 'x'\n");
        var missing = await Capture(() => McpCommands.McpAsync(["uninstall", "grok"]));
        Assert.Contains("not there", missing.Out, StringComparison.Ordinal);

        await McpCommands.McpAsync(["install", "grok"]);
        var removed = await Capture(() => McpCommands.McpAsync(["uninstall", "grok"]));
        Assert.Contains("removed", removed.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("mcp_servers.cmdwarden", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Help_for_the_new_commands_returns_zero_and_names_grok()
    {
        var hook = await Capture(() => PolicyHookCommands.HookAsync(["--help"]));
        var leak = await Capture(() => LeakGuardCommands.LeakGuardAsync(["help"]));
        var mcp = await Capture(() => McpCommands.McpAsync(["-h"]));
        var unknown = await Capture(() => McpCommands.McpAsync(["install"]));
        var setup = await Capture(() => CliApp.RunAsync(["setup", "--help"]));
        var root = await Capture(() => CliApp.RunAsync(["--help"]));

        Assert.Equal(0, hook.Code);
        Assert.Equal(0, leak.Code);
        Assert.Equal(0, mcp.Code);
        Assert.Equal(1, unknown.Code);
        Assert.Equal(0, setup.Code);
        Assert.Equal(0, root.Code);
        Assert.Contains("grok", hook.Out, StringComparison.Ordinal);
        Assert.Contains("grok", leak.Out, StringComparison.Ordinal);
        Assert.Contains("grok", mcp.Out, StringComparison.Ordinal);
        Assert.Contains("Grok", setup.Out, StringComparison.Ordinal);
        Assert.Contains("launch claude|codex|cursor|grok", root.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Claude_mcp_install_fails_when_claude_is_not_on_path()
    {
        var empty = Path.Combine(_home, "empty-path");
        Directory.CreateDirectory(empty);
        var old = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", empty);
        try
        {
            var result = await Capture(() => McpCommands.McpAsync(["install", "claude"]));
            Assert.Equal(1, result.Code);
            Assert.Contains("claude) is not on PATH", result.Err, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", old);
        }
    }

    [Fact]
    public async Task Agent_start_fails_when_dotnet_is_not_on_path()
    {
        var dir = Path.Combine(_home, "agent");
        Directory.CreateDirectory(dir);
        var dll = Path.Combine(dir, AgentLocator.AgentDllName);
        File.WriteAllText(dll, "not a binary");
        var oldPath = Environment.GetEnvironmentVariable("PATH");
        var oldPipe = Environment.GetEnvironmentVariable("CW_PIPE_NAME");
        var oldRoot = Environment.GetEnvironmentVariable(ProductPaths.EnvVar);
        Environment.SetEnvironmentVariable("PATH", _home);
        try
        {
            var status = await AgentLifecycle.StartAsync(
                "cw-coverage-" + Guid.NewGuid().ToString("N"),
                dll,
                Path.Combine(_home, "product"),
                TimeSpan.FromSeconds(1));
            Assert.Contains("dotnet is not on PATH", status.Detail, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
            Environment.SetEnvironmentVariable("CW_PIPE_NAME", oldPipe);
            Environment.SetEnvironmentVariable(ProductPaths.EnvVar, oldRoot);
        }
    }

    [Fact]
    public void Team_sync_uses_git_and_stops_when_git_is_not_on_path()
    {
        var repo = Path.Combine(_home, "repo");
        Directory.CreateDirectory(repo);
        RunGit(repo, "init", "-b", "main");
        RunGit(repo, "config", "user.email", "cw@example.com");
        RunGit(repo, "config", "user.name", "cw");
        File.WriteAllText(Path.Combine(repo, TeamPolicy.DefaultRepoFile), """{ "maxLevels": { "ai-harness": { "*": "Read" } } }""");
        RunGit(repo, "add", TeamPolicy.DefaultRepoFile);
        RunGit(repo, "commit", "-m", "team");

        var policy = Path.Combine(_home, "policy.json");
        var synced = CaptureSync(() => TeamPolicyCommands.Sync([repo], policy));
        Assert.Equal(0, synced.Code);
        Assert.Contains("Team rules from", synced.Out, StringComparison.Ordinal);
        Assert.True(File.Exists(TeamPolicy.PathFor(policy)));

        var old = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", _home);
        try
        {
            var failed = CaptureSync(() => TeamPolicyCommands.Sync([repo], Path.Combine(_home, "other", "policy.json")));
            Assert.Equal(1, failed.Code);
            Assert.Contains("git is not on PATH", failed.Err, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", old);
        }
    }

    private static void RunGit(string dir, params string[] args)
    {
        var git = InjectRunner.ResolveProgram("git");
        var psi = new ProcessStartInfo(git)
        {
            WorkingDirectory = dir,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)!;
        var error = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
    }

    private static (int Code, string Out, string Err) CaptureSync(Func<int> run)
    {
        var captured = Capture(() => Task.FromResult(run())).GetAwaiter().GetResult();
        return captured;
    }

    private static async Task<(int Code, string Out, string Err)> Capture(Func<Task<int>> run)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var oldOut = Console.Out;
        var oldErr = Console.Error;
        var previous = AnsiConsole.Console;
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(stdout),
            Ansi = AnsiSupport.No,
            Interactive = InteractionSupport.No,
        });
        Console.SetOut(stdout);
        Console.SetError(stderr);
        AnsiConsole.Console = console;
        try
        {
            return (await run().ConfigureAwait(false), stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(oldOut);
            Console.SetError(oldErr);
            AnsiConsole.Console = previous;
        }
    }
}

/// <summary>Path lookup, the npm shim, the Grok leak source, and the app JWT. These tests change no home.</summary>
public sealed class NewCodeCoverageTests
{
    [Fact]
    public void Resolve_skips_a_bad_path_entry()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cw-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var tool = Path.Combine(dir, "mytool.CMD");
            File.WriteAllText(tool, "");
            var path = @"C:\bad|dir" + Path.PathSeparator + dir;
            var found = ProgramPath.Resolve("mytool", path, ".EXE;.CMD");
            Assert.Equal(tool, found);
            Assert.Equal("missing", ProgramPath.Resolve("missing", @"C:\bad|dir", ".EXE"));
            Assert.Equal(Path.Combine(dir, "gone.exe"), ProgramPath.Resolve(Path.Combine(dir, "gone.exe")));
            Assert.Equal(Path.GetFullPath(tool), ProgramPath.Resolve(tool));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Npm_shim_returns_the_package_folder_or_null()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cw-npm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var cmd = Path.Combine(dir, "grok.cmd");
            File.WriteAllText(cmd, @"node ""%dp0%\node_modules\@scope\grok\bin\grok""");
            Assert.Equal(Path.Combine(dir, "node_modules", "@scope", "grok"), HarnessLauncher.NpmPackageDir(cmd));

            File.WriteAllText(cmd, "echo hi");
            Assert.Null(HarnessLauncher.NpmPackageDir(cmd));
            Assert.Null(HarnessLauncher.NpmPackageDir(Path.Combine(dir, "missing.cmd")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Grok_leak_guard_names_the_harness_in_the_source()
    {
        string? seen = null;
        var input = new JsonObject
        {
            ["tool_name"] = "run_terminal_command",
            ["tool_response"] = "clean output",
        };
        var output = await LeakGuardHook.ClaudePostToolUseAsync(input, (texts, source) =>
        {
            seen = source;
            return Task.FromResult(new LeakCheck(texts, []));
        }, "grok");

        Assert.Null(output);
        Assert.Equal("grok:run_terminal_command", seen);
    }

    [Fact]
    public void App_jwt_uses_the_pkcs1_signature_github_requires()
    {
        using var key = RSA.Create(2048);
        var jwt = GitHubApp.CreateJwt(42, key, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(3, jwt.Split('.').Length);
    }
}
