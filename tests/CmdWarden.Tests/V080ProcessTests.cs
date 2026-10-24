using System.Diagnostics;
using System.Text;
using CmdWarden.Cli;
using CmdWarden.Cli.Hooks;
using CmdWarden.Cli.Mcp;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// v0.8.0 and later through the real cw process and a real Session Agent: #65 cw try and cw setup, #67
/// cw inject --env-file, #69 masked output, #97 cw read and cw save --generate. Each test has its own
/// product root, home, and gate.
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class V080ProcessTests
{
    private sealed record CwRun(int Exit, string Out, string Err);

    private static string TryAgentExe() =>
        Path.Combine(TestPaths.FindShimOutputDir("CmdWarden.TryAgent", "cw-try-agent"), "cw-try-agent.exe");

    private static async Task<CwRun> RunCwAsync(ApprovalMemoryFixture fx, string home, IReadOnlyList<string> args,
        string? path = null, string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = workingDirectory ?? home,
        };
        psi.ArgumentList.Add(TestPaths.FindCliDll());
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        psi.Environment["CW_PIPE_NAME"] = fx.PipeName;
        psi.Environment["CW_POLICY_PATH"] = fx.PolicyPath;
        psi.Environment[ProductPaths.EnvVar] = fx.ProductRoot;
        psi.Environment[ProductPaths.HomeEnvVar] = home;
        psi.Environment["CW_TRY_AGENT_PATH"] = TryAgentExe();
        // A Session Agent that cw restarts keeps the scripted gate: no real card in a test run.
        psi.Environment["CW_APPROVAL_MODE"] = Environment.GetEnvironmentVariable("CW_TEST_APPROVAL_MODE") ?? "deny";
        if (path is not null)
        {
            foreach (var key in psi.Environment.Keys.Where(k => k.Equals("PATH", StringComparison.OrdinalIgnoreCase)).ToList())
                psi.Environment.Remove(key);
            psi.Environment["PATH"] = path;
        }
        using var p = Process.Start(psi)!;
        p.StandardInput.Close();
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return new CwRun(p.ExitCode, await stdout, await stderr);
    }

    private static string NewHome()
    {
        var home = Path.Combine(Path.GetTempPath(), "cw-home-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        return home;
    }

    [Fact]
    public async Task Try_shows_the_card_to_a_launcher_nobody_enrolled_and_prints_the_audit_row()
    {
        if (!OperatingSystem.IsWindows())
            return;
        // A person runs cw try from an enrolled terminal. An enrolled AI harness above would be the launcher instead.
        await using var fx = await ApprovalMemoryFixture.CreateAsync("deny", LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        var home = NewHome();

        var run = await RunCwAsync(fx, home, ["try"]);

        Assert.Equal(3, run.Exit);
        Assert.Contains("Blocked.", run.Out);
        Assert.Contains("CW_TRY_TOKEN", run.Out);
        var row = fx.AuditLines().Last(l => l.Contains("\"tool\":\"try\"", StringComparison.Ordinal));
        Assert.Contains("\"decision\":\"deny\"", row);
        Assert.True(row.Contains("\"policyLevel\":\"Deny\"", StringComparison.Ordinal), row);
        Assert.DoesNotContain(fx.SelectedPolicyKey, row);
    }

    [Fact]
    public async Task Try_approve_gives_the_fake_value_and_the_output_shows_only_the_placeholder()
    {
        if (!OperatingSystem.IsWindows())
            return;
        await using var fx = await ApprovalMemoryFixture.CreateAsync("allow", LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;

        var run = await RunCwAsync(fx, NewHome(), ["try"]);

        Assert.Equal(0, run.Exit);
        Assert.Contains("The agent read the token: [CmdWarden: CW_TRY_TOKEN]", run.Out);
        Assert.DoesNotContain("cw-try-", run.Out);
    }

    [Fact]
    public async Task Inject_env_file_releases_the_set_with_one_card_and_masks_the_output()
    {
        if (!OperatingSystem.IsWindows())
            return;
        await using var fx = await ApprovalMemoryFixture.CreateAsync("allow");
        if (fx is null)
            return;
        var first = "CW_ENV_A_" + Guid.NewGuid().ToString("N")[..6];
        var second = "CW_ENV_B_" + Guid.NewGuid().ToString("N")[..6];
        var firstValue = "first-" + Guid.NewGuid().ToString("N");
        var secondValue = "second-" + Guid.NewGuid().ToString("N");
        await AgentVaultClient.SaveAsync(first, Encoding.UTF8.GetBytes(firstValue), fx.PipeName);
        await AgentVaultClient.SaveAsync(second, Encoding.UTF8.GetBytes(secondValue), fx.PipeName);
        var home = NewHome();
        File.WriteAllText(Path.Combine(home, ".env"), $"# app\nONE=cw://{first}\nTWO=cw://{second}\nPLAIN=hello\n");
        try
        {
            var before = fx.AuditLines().Count;
            var run = await RunCwAsync(fx, home, ["inject", "--env-file", ".env", "--", "cmd.exe", "/c", "echo %ONE% %TWO% %PLAIN%"]);

            Assert.Equal(0, run.Exit);
            Assert.Contains($"[CmdWarden: {first}] [CmdWarden: {second}] hello", run.Out);
            Assert.DoesNotContain(firstValue, run.Out);
            var rows = fx.AuditLines().Skip(before).Where(l => l.Contains(first, StringComparison.Ordinal)).ToList();
            Assert.Single(rows);
            Assert.Contains($"{first}, {second}", rows[0]);

            var raw = await RunCwAsync(fx, home, ["inject", "--env-file", ".env", "--no-masking", "--", "cmd.exe", "/c", "echo %ONE%"]);
            Assert.Contains(firstValue, raw.Out);

            File.AppendAllText(Path.Combine(home, ".env"), "THREE=cw://CW_ENV_MISSING\n");
            var missing = await RunCwAsync(fx, home, ["inject", "--env-file", ".env", "--", "cmd.exe", "/c", "echo x"]);
            Assert.Equal(1, missing.Exit);
            Assert.Contains("No vault entry named CW_ENV_MISSING", missing.Err);
        }
        finally
        {
            await AgentVaultClient.DeleteAsync(first, fx.PipeName);
            await AgentVaultClient.DeleteAsync(second, fx.PipeName);
        }
    }

    [Fact]
    public async Task Read_shows_one_value_as_secret_reveal_and_names_a_missing_entry_before_the_card()
    {
        if (!OperatingSystem.IsWindows())
            return;
        await using var fx = await ApprovalMemoryFixture.CreateAsync("allow");
        if (fx is null)
            return;
        var name = "CW_READ_" + Guid.NewGuid().ToString("N")[..6];
        var value = "read-" + Guid.NewGuid().ToString("N");
        await AgentVaultClient.SaveAsync(name, Encoding.UTF8.GetBytes(value), fx.PipeName);
        var home = NewHome();
        try
        {
            var run = await RunCwAsync(fx, home, ["read", name]);
            Assert.Equal(0, run.Exit);
            Assert.Equal(value + "\n", run.Out);
            Assert.Contains(fx.AuditLines(), l => l.Contains(name, StringComparison.Ordinal) && l.Contains(CommandClassNames.SecretReveal, StringComparison.Ordinal));

            var bare = await RunCwAsync(fx, home, ["read", name, "--no-newline"]);
            Assert.Equal(value, bare.Out);

            var before = fx.AuditLines().Count;
            var missing = await RunCwAsync(fx, home, ["read", "CW_READ_MISSING"]);
            Assert.Equal(1, missing.Exit);
            Assert.Contains("No vault entry named CW_READ_MISSING", missing.Err);
            Assert.Equal(before, fx.AuditLines().Count);
        }
        finally
        {
            await AgentVaultClient.DeleteAsync(name, fx.PipeName);
        }
    }

    [Fact]
    public async Task Inject_template_fills_a_private_copy_for_the_run_and_deletes_it()
    {
        if (!OperatingSystem.IsWindows())
            return;
        await using var fx = await ApprovalMemoryFixture.CreateAsync("allow");
        if (fx is null)
            return;
        var name = "CW_TPL_" + Guid.NewGuid().ToString("N")[..6];
        var value = "tpl-" + Guid.NewGuid().ToString("N");
        await AgentVaultClient.SaveAsync(name, Encoding.UTF8.GetBytes(value), fx.PipeName);
        var home = NewHome();
        var template = Path.Combine(home, "app.yml");
        File.WriteAllText(template, $"db:\n  password: cw://{name}\n  user: me\n");
        var tmp = Path.Combine(fx.ProductRoot, "tmp");
        try
        {
            var run = await RunCwAsync(fx, home, ["inject", "--template", "app.yml", "--", "cmd.exe", "/c", "type {template} & echo %CW_TEMPLATE%"]);
            Assert.Equal(0, run.Exit);
            Assert.Contains($"password: [CmdWarden: {name}]", run.Out);
            Assert.DoesNotContain(value, run.Out);
            var copy = run.Out.Split('\n').Select(l => l.Trim()).Single(l => l.EndsWith("app.yml", StringComparison.OrdinalIgnoreCase));
            Assert.StartsWith(tmp, copy, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(copy));
            Assert.Empty(Directory.GetDirectories(tmp));
            Assert.Contains($"cw://{name}", File.ReadAllText(template));

            var raw = await RunCwAsync(fx, home, ["inject", "--template", "app.yml", "--no-masking", "--", "cmd.exe", "/c", "type", "{template}"]);
            Assert.Contains($"password: {value}", raw.Out);

            var none = Path.Combine(home, "none.yml");
            File.WriteAllText(none, "no placeholders\n");
            var empty = await RunCwAsync(fx, home, ["inject", "--template", "none.yml", "--", "cmd.exe", "/c", "echo x"]);
            Assert.Equal(1, empty.Exit);
            Assert.Contains("No vault names", empty.Err);
        }
        finally
        {
            await AgentVaultClient.DeleteAsync(name, fx.PipeName);
        }
    }

    [Fact]
    public void A_template_copy_is_only_for_this_user_and_a_dead_run_leaves_none()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var root = Path.Combine(Path.GetTempPath(), "cw-tpl-" + Guid.NewGuid().ToString("N"));
        var leftover = Path.Combine(root, "tmp", "999999_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(leftover);
        try
        {
            using var copy = TemplateFile.Write("app.json", "{\"k\":\"cw://A\"}", new Dictionary<string, string> { ["A"] = "v\"1" }, root);
            // The value goes in as it is, like op inject: the file format is not read.
            Assert.Equal("{\"k\":\"v\"1\"}", File.ReadAllText(copy.Path));
            Assert.False(Directory.Exists(leftover));
            var rules = new DirectoryInfo(Path.GetDirectoryName(copy.Path)!).GetAccessControl()
                .GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier)).Cast<System.Security.AccessControl.FileSystemAccessRule>().ToList();
            var me = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
            Assert.All(rules, r => Assert.Equal(me, r.IdentityReference));
            Assert.Equal(["x", "--config=" + copy.Path], copy.Arguments(["x", "--config={template}"]));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Save_generate_keeps_the_password_hidden_and_an_entry_that_exists()
    {
        if (!OperatingSystem.IsWindows())
            return;
        await using var fx = await ApprovalMemoryFixture.CreateAsync("allow");
        if (fx is null)
            return;
        var name = "CW_GEN_" + Guid.NewGuid().ToString("N")[..6];
        var home = NewHome();
        try
        {
            var run = await RunCwAsync(fx, home, ["save", name, "--generate"]);
            Assert.Equal(0, run.Exit);
            var first = (await AgentVaultClient.ReleaseAsync(name, pipeName: fx.PipeName)).Value.ToStringUtf8();
            Assert.Equal(PasswordGenerator.DefaultLength, first.Length);
            Assert.DoesNotContain(first, run.Out + run.Err);

            var again = await RunCwAsync(fx, home, ["save", name, "--generate"]);
            Assert.Equal(1, again.Exit);
            Assert.Contains("--force", again.Err);
            Assert.Equal(first, (await AgentVaultClient.ReleaseAsync(name, pipeName: fx.PipeName)).Value.ToStringUtf8());

            var forced = await RunCwAsync(fx, home, ["save", name, "--generate", "--length", "40", "--force"]);
            Assert.Equal(0, forced.Exit);
            Assert.Equal(40, (await AgentVaultClient.ReleaseAsync(name, pipeName: fx.PipeName)).Value.ToStringUtf8().Length);
        }
        finally
        {
            await AgentVaultClient.DeleteAsync(name, fx.PipeName);
        }
    }

    [Fact]
    public async Task Setup_wires_a_found_harness_and_a_second_run_changes_nothing()
    {
        if (!OperatingSystem.IsWindows())
            return;
        await using var fx = await ApprovalMemoryFixture.CreateAsync("deny");
        if (fx is null)
            return;
        var home = NewHome();
        var bin = Path.Combine(home, "bin");
        Directory.CreateDirectory(bin);
        var tryAgentDir = Path.GetDirectoryName(TryAgentExe())!;
        File.Copy(TryAgentExe(), Path.Combine(bin, "codex.exe"));
        foreach (var file in Directory.EnumerateFiles(tryAgentDir, "cw-try-agent.*").Where(f => !f.EndsWith(".exe", StringComparison.Ordinal)))
            File.Copy(file, Path.Combine(bin, Path.GetFileName(file)));
        var path = $"{bin};{Environment.SystemDirectory};{Path.GetDirectoryName(CmdWarden.Cli.InjectRunner.ResolveProgram("dotnet"))}";
        string[] args = ["setup", "--yes", "--skip", "terminal,tools,shortcuts,canary,try"];
        try
        {
            var first = await RunCwAsync(fx, home, args, path);

            Assert.True(first.Exit == 0, first.Out + first.Err);
            Assert.Contains("harness codex", first.Out);
            var hooks = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(HookInstaller.CodexHooksPath(home)))!;
            Assert.Contains("hook check codex", hooks["hooks"]!["PreToolUse"]!.ToJsonString());
            Assert.Contains("leak-guard codex", hooks["hooks"]!["PostToolUse"]!.ToJsonString());
            var config = File.ReadAllText(CodexConfig.Path(home));
            Assert.Contains($"[mcp_servers.{McpCommands.ServerName}]", config);
            Assert.Contains("ignore_default_excludes = false", config);
            var store = new PolicyStore(fx.PolicyPath);
            store.Load();
            Assert.Contains(store.Launchers.Values, e => e.Path?.EndsWith("codex.exe", StringComparison.OrdinalIgnoreCase) == true);

            var hooksBefore = File.ReadAllText(HookInstaller.CodexHooksPath(home));
            var second = await RunCwAsync(fx, home, args, path);
            Assert.True(second.Exit == 0, second.Out + second.Err);
            Assert.Contains("already there", second.Out);
            Assert.Equal(hooksBefore, File.ReadAllText(HookInstaller.CodexHooksPath(home)));
            Assert.Equal(config, File.ReadAllText(CodexConfig.Path(home)));
        }
        finally
        {
            // Enrolling a Codex sandbox account restarts the agent on this pipe; stop that one too.
            await AgentLifecycle.StopAsync(fx.PipeName);
        }
    }
}
