using System.Diagnostics;
using System.Text;
using CmdWarden.Agent.Approval;
using CmdWarden.Cli;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// PATH shim client: Authorize → spawn (issue #32).
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class GhShimProcessTests
{
    [Fact]
    public async Task Shim_exits_agent_down_when_no_agent()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var noAgent = new NoAgentBinary();
        var missingPipe = $"{AgentEndpoints.PipeNamePrefix}-missing-shim-{Guid.NewGuid():N}";
        var exit = await GhShimApp.RunAsync(
            new[] { "pr", "list" },
            pipeName: missingPipe,
            timeout: TimeSpan.FromMilliseconds(800));

        Assert.Equal(GhShimApp.ExitAgentDown, exit);
    }

    [Fact]
    public async Task Shim_on_grant_spawns_real_with_child_env_only()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var pipeName = $"{AgentEndpoints.PipeNamePrefix}-shim-{Guid.NewGuid():N}";
        var productRoot = Path.Combine(Path.GetTempPath(), "cw-prod-" + Guid.NewGuid().ToString("N"));
        var policyPath = Path.Combine(productRoot, "policy.json");
        Directory.CreateDirectory(productRoot);

        // Stand-in "real gh": requires GH_TOKEN + GH_PATH (child overlay only).
        var realGh = Path.GetFullPath(Path.Combine(productRoot, "real-gh.cmd"));
        await File.WriteAllTextAsync(realGh, $"""
            @echo off
            if "%GH_TOKEN%"=="" exit /b 7
            if /I not "%GH_PATH%"=="{realGh}" exit /b 9
            if /I not "%~1"=="pr" exit /b 8
            echo shim-ok
            exit /b 0
            """);

        var tokenValue = "ghp_shim_" + Guid.NewGuid().ToString("N");
        var agentDll = TestPaths.FindAgentDll();

        Environment.SetEnvironmentVariable("CW_PIPE_NAME", pipeName);
        Environment.SetEnvironmentVariable("CW_POLICY_PATH", policyPath);
        Environment.SetEnvironmentVariable(ProductPaths.EnvVar, productRoot);
        // Parent must not already have the token or GH_PATH from a prior run
        var previousParentToken = Environment.GetEnvironmentVariable("GH_TOKEN");
        var previousParentPath = Environment.GetEnvironmentVariable("GH_PATH");
        Environment.SetEnvironmentVariable("GH_TOKEN", null);
        Environment.SetEnvironmentVariable("GH_PATH", null);
        try
        {
            await using var agent = await AgentProcess.StartAsync(
                agentDll, pipeName, policyPath, productRoot, approvalMode: "off");

            var id = await AgentHealthClient.ResolveIdentityAsync(pipeName);
            Assert.True(id.AutoApproveEligible);

            var store = new PolicyStore(policyPath);
            store.Load();
            store.Enroll(id.SelectedPolicyKey, LauncherEnrollmentKind.Terminal);
            store.Save();

            new ToolPinStore(productRoot).Save("gh", realGh);
            await AgentVaultClient.SaveAsync("GH_TOKEN", Encoding.UTF8.GetBytes(tokenValue), pipeName);

            var exit = await GhShimApp.RunAsync(
                new[] { "pr", "list" },
                pipeName: pipeName,
                timeout: TimeSpan.FromSeconds(30));

            Assert.Equal(0, exit);
            // Parent still has no token / GH_PATH from shim (child-only env overlay)
            Assert.NotEqual(tokenValue, Environment.GetEnvironmentVariable("GH_TOKEN"));
            Assert.NotEqual(realGh, Environment.GetEnvironmentVariable("GH_PATH"));
        }
        finally
        {
            try { await AgentVaultClient.DeleteAsync("GH_TOKEN", pipeName); } catch { /* ignore */ }
            Environment.SetEnvironmentVariable("CW_PIPE_NAME", null);
            Environment.SetEnvironmentVariable("CW_POLICY_PATH", null);
            Environment.SetEnvironmentVariable(ProductPaths.EnvVar, null);
            Environment.SetEnvironmentVariable("GH_TOKEN", previousParentToken);
            Environment.SetEnvironmentVariable("GH_PATH", previousParentPath);
            try { Directory.Delete(productRoot, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Shim_on_deny_exits_nonzero_without_spawning_secret()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var pipeName = $"{AgentEndpoints.PipeNamePrefix}-shimdeny-{Guid.NewGuid():N}";
        var productRoot = Path.Combine(Path.GetTempPath(), "cw-prod-" + Guid.NewGuid().ToString("N"));
        var policyPath = Path.Combine(productRoot, "policy.json");
        Directory.CreateDirectory(productRoot);

        var realGh = Path.Combine(productRoot, "real-gh.cmd");
        await File.WriteAllTextAsync(realGh, """
            @echo off
            rem if this runs, fail the test scenario
            exit /b 0
            """);

        var agentDll = TestPaths.FindAgentDll();
        Environment.SetEnvironmentVariable("CW_PIPE_NAME", pipeName);
        Environment.SetEnvironmentVariable("CW_POLICY_PATH", policyPath);
        Environment.SetEnvironmentVariable(ProductPaths.EnvVar, productRoot);
        try
        {
            await using var agent = await AgentProcess.StartAsync(
                agentDll, pipeName, policyPath, productRoot, approvalMode: "off");

            var id = await AgentHealthClient.ResolveIdentityAsync(pipeName);
            if (!id.AutoApproveEligible)
                return;

            // No enrollment → Deny path under approval off
            new ToolPinStore(productRoot).Save("gh", realGh);
            await AgentVaultClient.SaveAsync("GH_TOKEN", Encoding.UTF8.GetBytes("secret"), pipeName);

            var exit = await GhShimApp.RunAsync(
                new[] { "pr", "list" },
                pipeName: pipeName,
                timeout: TimeSpan.FromSeconds(30));

            Assert.Equal(GhShimApp.ExitDenied, exit);
        }
        finally
        {
            try { await AgentVaultClient.DeleteAsync("GH_TOKEN", pipeName); } catch { /* ignore */ }
            Environment.SetEnvironmentVariable("CW_PIPE_NAME", null);
            Environment.SetEnvironmentVariable("CW_POLICY_PATH", null);
            Environment.SetEnvironmentVariable(ProductPaths.EnvVar, null);
            try { Directory.Delete(productRoot, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Shim_user_deny_prints_a_plain_sentence()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var (exit, err) = await RunStoppedShimAsync("deny");
        if (exit is null)
            return;

        Assert.Equal(GhShimApp.ExitDenied, exit);
        Assert.Equal("CmdWarden: you denied this gh command." + Environment.NewLine, err);
        Assert.DoesNotContain("UserDenied", err, StringComparison.Ordinal);
        Assert.DoesNotContain("launcher=", err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shim_gate_unavailable_prints_the_timeout_sentence()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var (exit, err) = await RunStoppedShimAsync("off");
        if (exit is null)
            return;

        Assert.Equal(GhShimApp.ExitDenied, exit);
        Assert.Equal(
            "CmdWarden: the Approval Gate timed out. The gh command did not run." + Environment.NewLine,
            err);
        Assert.DoesNotContain("ApprovalUnavailable", err, StringComparison.Ordinal);
    }

    private static async Task<(int? Exit, string Error)> RunStoppedShimAsync(string approvalMode)
    {
        var pipeName = $"{AgentEndpoints.PipeNamePrefix}-shimstop-{Guid.NewGuid():N}";
        var productRoot = Path.Combine(Path.GetTempPath(), "cw-prod-" + Guid.NewGuid().ToString("N"));
        var policyPath = Path.Combine(productRoot, "policy.json");
        Directory.CreateDirectory(productRoot);

        var realGh = Path.Combine(productRoot, "real-gh.cmd");
        await File.WriteAllTextAsync(realGh, "@echo off\r\nexit /b 0\r\n");

        Environment.SetEnvironmentVariable("CW_PIPE_NAME", pipeName);
        Environment.SetEnvironmentVariable("CW_POLICY_PATH", policyPath);
        Environment.SetEnvironmentVariable(ProductPaths.EnvVar, productRoot);
        var err = new StringWriter();
        var previous = Console.Error;
        try
        {
            await using var agent = await AgentProcess.StartAsync(
                TestPaths.FindAgentDll(), pipeName, policyPath, productRoot, approvalMode);
            var id = await AgentHealthClient.ResolveIdentityAsync(pipeName);
            if (!id.AutoApproveEligible)
                return (null, "");

            var store = new PolicyStore(policyPath);
            store.Load();
            store.Enroll(id.SelectedPolicyKey, LauncherEnrollmentKind.AiHarness);
            store.Save();
            new ToolPinStore(productRoot).Save("gh", realGh);
            await AgentVaultClient.SaveAsync("GH_TOKEN", Encoding.UTF8.GetBytes("secret-value"), pipeName);

            Console.SetError(err);
            var exit = await GhShimApp.RunAsync(
                new[] { "pr", "create" },
                pipeName: pipeName,
                timeout: TimeSpan.FromSeconds(30));
            return (exit, err.ToString());
        }
        finally
        {
            Console.SetError(previous);
            try { await AgentVaultClient.DeleteAsync("GH_TOKEN", pipeName); } catch { /* ignore */ }
            Environment.SetEnvironmentVariable("CW_PIPE_NAME", null);
            Environment.SetEnvironmentVariable("CW_POLICY_PATH", null);
            Environment.SetEnvironmentVariable(ProductPaths.EnvVar, null);
            try { Directory.Delete(productRoot, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Deny_stops_new_prompts_for_retries_from_the_same_launcher()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var pipeName = $"{AgentEndpoints.PipeNamePrefix}-denyloop-{Guid.NewGuid():N}";
        var productRoot = Path.Combine(Path.GetTempPath(), "cw-prod-" + Guid.NewGuid().ToString("N"));
        var policyPath = Path.Combine(productRoot, "policy.json");
        Directory.CreateDirectory(productRoot);

        var realGh = Path.Combine(productRoot, "real-gh.cmd");
        await File.WriteAllTextAsync(realGh, "@echo off\r\nexit /b 0\r\n");
        // Stand-in Approval Gate: count each popup, then answer Deny.
        var countFile = Path.Combine(productRoot, "prompts.txt");
        var helper = Path.Combine(productRoot, "gate.cmd");
        await File.WriteAllTextAsync(helper,
            $"@echo off\r\necho x>>\"{countFile}\"\r\nexit /b {ApprovalHelperExitCodes.Deny}\r\n");

        Environment.SetEnvironmentVariable("CW_PIPE_NAME", pipeName);
        Environment.SetEnvironmentVariable("CW_POLICY_PATH", policyPath);
        Environment.SetEnvironmentVariable(ProductPaths.EnvVar, productRoot);
        var previous = Console.Error;
        try
        {
            await using var agent = await AgentProcess.StartAsync(
                TestPaths.FindAgentDll(), pipeName, policyPath, productRoot, approvalMode: "prompt",
                helperPath: helper);
            var id = await AgentHealthClient.ResolveIdentityAsync(pipeName);
            if (!id.AutoApproveEligible)
                return;

            var store = new PolicyStore(policyPath);
            store.Load();
            store.Enroll(id.SelectedPolicyKey, LauncherEnrollmentKind.AiHarness);
            store.Save();
            new ToolPinStore(productRoot).Save("gh", realGh);
            await AgentVaultClient.SaveAsync("GH_TOKEN", Encoding.UTF8.GetBytes("secret-value"), pipeName);

            Console.SetError(TextWriter.Null);
            Task<int> Run(params string[] args) =>
                GhShimApp.RunAsync(args, pipeName: pipeName, timeout: TimeSpan.FromSeconds(30));

            Assert.Equal(GhShimApp.ExitDenied, await Run("pr", "create"));
            // An AI harness retries with other arguments, one by one and in parallel.
            Assert.Equal(GhShimApp.ExitDenied, await Run("pr", "create", "--draft"));
            Assert.Equal(GhShimApp.ExitDenied, await Run("issue", "create"));
            var parallel = await Task.WhenAll(Run("pr", "merge", "1"), Run("pr", "merge", "2"), Run("pr", "merge", "3"));
            Assert.All(parallel, exit => Assert.Equal(GhShimApp.ExitDenied, exit));

            Assert.Single(await File.ReadAllLinesAsync(countFile));
        }
        finally
        {
            Console.SetError(previous);
            try { await AgentVaultClient.DeleteAsync("GH_TOKEN", pipeName); } catch { /* ignore */ }
            Environment.SetEnvironmentVariable("CW_PIPE_NAME", null);
            Environment.SetEnvironmentVariable("CW_POLICY_PATH", null);
            Environment.SetEnvironmentVariable(ProductPaths.EnvVar, null);
            try { Directory.Delete(productRoot, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Shim_keyring_auth_commands_get_GH_PATH_and_no_token()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var pipeName = $"{AgentEndpoints.PipeNamePrefix}-shimauth-{Guid.NewGuid():N}";
        var productRoot = Path.Combine(Path.GetTempPath(), "cw-prod-" + Guid.NewGuid().ToString("N"));
        var policyPath = Path.Combine(productRoot, "policy.json");
        Directory.CreateDirectory(productRoot);

        // Stand-in "real gh": exit 7 when GH_TOKEN is set, 9 when GH_PATH is wrong (#198).
        var realGh = Path.GetFullPath(Path.Combine(productRoot, "real-gh.cmd"));
        await File.WriteAllTextAsync(realGh, $"""
            @echo off
            if not "%GH_TOKEN%"=="" exit /b 7
            if /I not "%GH_PATH%"=="{realGh}" exit /b 9
            exit /b 0
            """);

        var tokenValue = "ghp_auth_" + Guid.NewGuid().ToString("N");
        var agentDll = TestPaths.FindAgentDll();

        Environment.SetEnvironmentVariable("CW_PIPE_NAME", pipeName);
        Environment.SetEnvironmentVariable("CW_POLICY_PATH", policyPath);
        Environment.SetEnvironmentVariable(ProductPaths.EnvVar, productRoot);
        var previousParentToken = Environment.GetEnvironmentVariable("GH_TOKEN");
        var previousParentPath = Environment.GetEnvironmentVariable("GH_PATH");
        Environment.SetEnvironmentVariable("GH_TOKEN", null);
        Environment.SetEnvironmentVariable("GH_PATH", null);
        try
        {
            await using var agent = await AgentProcess.StartAsync(
                agentDll, pipeName, policyPath, productRoot, approvalMode: "off");

            var id = await AgentHealthClient.ResolveIdentityAsync(pipeName);
            Assert.True(id.AutoApproveEligible);

            var store = new PolicyStore(policyPath);
            store.Load();
            store.Enroll(id.SelectedPolicyKey, LauncherEnrollmentKind.Terminal);
            store.Save();

            new ToolPinStore(productRoot).Save("gh", realGh);
            await AgentVaultClient.SaveAsync("GH_TOKEN", Encoding.UTF8.GetBytes(tokenValue), pipeName);

            foreach (var sub in new[] { "login", "refresh", "logout", "switch" })
            {
                var exit = await GhShimApp.RunAsync(
                    new[] { "auth", sub, "--hostname", "github.com" },
                    pipeName: pipeName,
                    timeout: TimeSpan.FromSeconds(30));
                Assert.True(exit == 0, $"gh auth {sub}: exit {exit}");
            }

            // Non-auth command still carries the token: the stand-in exits 7.
            var listExit = await GhShimApp.RunAsync(
                new[] { "pr", "list" },
                pipeName: pipeName,
                timeout: TimeSpan.FromSeconds(30));
            Assert.Equal(7, listExit);

            var rows = new AuditLog(productRoot).ReadRecentLines(10)
                .Select(AuditGateRecord.TryParse).Where(r => r is not null).Select(r => r!).ToList();
            var authRows = rows.Where(r => r.CommandClass == CommandClassNames.Write).ToList();
            Assert.Equal(4, authRows.Count);
            Assert.All(authRows, r => Assert.Null(r.SecretName));
            Assert.Contains(rows, r => r.CommandClass == CommandClassNames.Read && r.SecretName == "GH_TOKEN");
        }
        finally
        {
            try { await AgentVaultClient.DeleteAsync("GH_TOKEN", pipeName); } catch { /* ignore */ }
            Environment.SetEnvironmentVariable("CW_PIPE_NAME", null);
            Environment.SetEnvironmentVariable("CW_POLICY_PATH", null);
            Environment.SetEnvironmentVariable(ProductPaths.EnvVar, null);
            Environment.SetEnvironmentVariable("GH_TOKEN", previousParentToken);
            Environment.SetEnvironmentVariable("GH_PATH", previousParentPath);
            try { Directory.Delete(productRoot, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void SpawnReal_sets_env_only_on_child()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var marker = "CW_SHIM_CHILD_" + Guid.NewGuid().ToString("N")[..8];
        Environment.SetEnvironmentVariable(marker, null);
        var exit = GhShimApp.SpawnReal(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            new[] { "/c", $"if defined {marker} (exit 0) else (exit 9)" },
            new Dictionary<string, string> { [marker] = "1" });

        Assert.Equal(0, exit);
        Assert.Null(Environment.GetEnvironmentVariable(marker));
    }



    private sealed class AgentProcess : IAsyncDisposable
    {
        private readonly Process _process;

        private AgentProcess(Process process) => _process = process;

        public static async Task<AgentProcess> StartAsync(
            string agentDll,
            string pipeName,
            string policyPath,
            string productRoot,
            string approvalMode = "off",
            string? helperPath = null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                ArgumentList = { agentDll },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.Environment["CW_PIPE_NAME"] = pipeName;
            psi.Environment["CW_POLICY_PATH"] = policyPath;
            psi.Environment[ProductPaths.EnvVar] = productRoot;
            psi.Environment[ApprovalGateFactory.EnvVar] = approvalMode;
            if (helperPath is not null)
                psi.Environment[ProcessApprovalGate.HelperPathEnvVar] = helperPath;

            var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start agent.");

            var deadline = DateTime.UtcNow.AddSeconds(25);
            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    var err = await process.StandardError.ReadToEndAsync();
                    var stdout = await process.StandardOutput.ReadToEndAsync();
                    throw new InvalidOperationException($"Agent exited: {stdout}{err}");
                }

                try
                {
                    _ = await AgentHealthClient.GetHealthAsync(pipeName, TimeSpan.FromMilliseconds(500));
                    return new AgentProcess(process);
                }
                catch
                {
                    await Task.Delay(150);
                }
            }

            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException("Agent did not become healthy.");
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
            catch { /* ignore */ }

            await _process.WaitForExitAsync();
            _process.Dispose();
        }
    }
}
