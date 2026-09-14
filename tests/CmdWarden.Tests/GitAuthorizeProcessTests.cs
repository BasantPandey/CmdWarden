using System.Diagnostics;
using CmdWarden.Agent.Approval;
using CmdWarden.Cli;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Primary seam: Authorize for tool=git (gate only, issue #40).
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class GitAuthorizeProcessTests
{
    [Fact]
    public async Task Authorize_git_read_auto_allows_Trusted_without_secret_map()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await GitAuthFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.Terminal);
        fx.PinCmdAsGit();

        var grant = await AgentAuthorizeClient.AuthorizeAsync(
            "git",
            new[] { "status" },
            pipeName: fx.PipeName);

        Assert.True(grant.Allowed);
        Assert.Equal("git", grant.Tool);
        Assert.Equal(CommandClassNames.Read, grant.CommandClass);
        Assert.Equal(PolicyLevelNames.Trusted, grant.PolicyLevel);
        Assert.Equal("auto-allow", grant.Decision);
        Assert.False(string.IsNullOrWhiteSpace(grant.RealPath));
        Assert.Empty(grant.Env);

        var audit = ReadLatestAudit(fx.ProductRoot);
        Assert.Contains("auto-allow", audit, StringComparison.Ordinal);
        Assert.Contains("\"tool\":\"git\"", audit, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"commandClass\":\"read\"", audit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ghp_", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("password", audit, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Authorize_git_push_write_auto_allows_Trusted_without_secret_map()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await GitAuthFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.Terminal);
        fx.PinCmdAsGit();

        var grant = await AgentAuthorizeClient.AuthorizeAsync(
            "git",
            new[] { "push", "origin", "main" },
            pipeName: fx.PipeName);

        Assert.True(grant.Allowed);
        Assert.Equal(CommandClassNames.Write, grant.CommandClass);
        Assert.Equal("auto-allow", grant.Decision);
        Assert.Empty(grant.Env);
    }

    [Fact]
    public async Task Authorize_git_credential_fill_not_auto_allowed_under_Trusted()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await GitAuthFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.Terminal);
        fx.PinCmdAsGit();

        var ex = await Assert.ThrowsAsync<Grpc.Core.RpcException>(async () =>
            await AgentAuthorizeClient.AuthorizeAsync(
                "git",
                new[] { "credential", "fill" },
                pipeName: fx.PipeName));

        Assert.Equal(Grpc.Core.StatusCode.PermissionDenied, ex.StatusCode);
        Assert.Contains(PolicyReasonCodes.ApprovalUnavailable, ex.Status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authorize_git_credential_manager_get_not_auto_allowed_under_Trusted()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await GitAuthFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.Terminal);
        fx.PinCmdAsGit();

        var ex = await Assert.ThrowsAsync<Grpc.Core.RpcException>(async () =>
            await AgentAuthorizeClient.AuthorizeAsync(
                "git",
                new[] { "credential-manager", "get" },
                pipeName: fx.PipeName));

        Assert.Equal(Grpc.Core.StatusCode.PermissionDenied, ex.StatusCode);
        Assert.Contains("secret-reveal", ex.Status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authorize_git_secret_reveal_not_auto_allowed_under_Read_ai_harness()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await GitAuthFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.AiHarness);
        fx.PinCmdAsGit();

        var ex = await Assert.ThrowsAsync<Grpc.Core.RpcException>(async () =>
            await AgentAuthorizeClient.AuthorizeAsync(
                "git",
                new[] { "credential", "fill" },
                pipeName: fx.PipeName));

        Assert.Equal(Grpc.Core.StatusCode.PermissionDenied, ex.StatusCode);
    }

    [Fact]
    public async Task Authorize_git_write_not_auto_allowed_under_Read()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await GitAuthFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.AiHarness); // default Read
        fx.PinCmdAsGit();

        var ex = await Assert.ThrowsAsync<Grpc.Core.RpcException>(async () =>
            await AgentAuthorizeClient.AuthorizeAsync(
                "git",
                new[] { "push" },
                pipeName: fx.PipeName));

        Assert.Equal(Grpc.Core.StatusCode.PermissionDenied, ex.StatusCode);
    }

    [Fact]
    public async Task Authorize_git_denies_when_pin_missing()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await GitAuthFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.Terminal);

        var ex = await Assert.ThrowsAsync<Grpc.Core.RpcException>(async () =>
            await AgentAuthorizeClient.AuthorizeAsync(
                "git",
                new[] { "status" },
                pipeName: fx.PipeName));

        Assert.Equal(Grpc.Core.StatusCode.FailedPrecondition, ex.StatusCode);
        Assert.Contains(PolicyReasonCodes.PinMissing, ex.Status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authorize_git_denies_when_pin_hash_mismatches()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await GitAuthFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.Terminal);
        var pinTarget = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        new ToolPinStore(fx.ProductRoot).Save("git", pinTarget, sha256Hex: new string('a', 64));

        var ex = await Assert.ThrowsAsync<Grpc.Core.RpcException>(async () =>
            await AgentAuthorizeClient.AuthorizeAsync(
                "git",
                new[] { "status" },
                pipeName: fx.PipeName));

        Assert.Equal(Grpc.Core.StatusCode.FailedPrecondition, ex.StatusCode);
        Assert.Contains(PolicyReasonCodes.PinMismatch, ex.Status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authorize_git_help_grants_without_env_or_vault()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await GitAuthFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.Terminal);
        fx.PinCmdAsGit();

        var grant = await AgentAuthorizeClient.AuthorizeAsync(
            "git",
            new[] { "push", "--help" },
            pipeName: fx.PipeName);

        Assert.True(grant.Allowed);
        Assert.Equal(CommandClassNames.Read, grant.CommandClass);
        Assert.Empty(grant.Env);
    }

    [Fact]
    public async Task Authorize_git_secret_reveal_allow_once_still_no_secret_map()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await GitAuthFixture.CreateAsync(approvalMode: "allow");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.Terminal);
        fx.PinCmdAsGit();

        var grant = await AgentAuthorizeClient.AuthorizeAsync(
            "git",
            new[] { "credential", "fill" },
            pipeName: fx.PipeName);

        Assert.True(grant.Allowed);
        Assert.Equal(CommandClassNames.SecretReveal, grant.CommandClass);
        Assert.Equal("allow-once", grant.Decision);
        Assert.Empty(grant.Env);

        var audit = ReadLatestAudit(fx.ProductRoot);
        Assert.Contains("allow-once", audit, StringComparison.Ordinal);
        Assert.Contains("secret-reveal", audit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", audit, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Strong_grant_strips_config_env_and_classifies_config_key_like_dash_c()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await GitAuthFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.Terminal);
        fx.PinCmdAsGit();
        var compat = await AgentAuthorizeClient.AuthorizeAsync("git", new[] { "status" }, pipeName: fx.PipeName);
        Assert.Empty(compat.StripEnv);

        new ToolPinStore(fx.ProductRoot).SetMode("git", ToolPin.StrongMode);
        var strong = await AgentAuthorizeClient.AuthorizeAsync("git", new[] { "status" }, pipeName: fx.PipeName);
        Assert.Equal(GitCommandClassifier.StrongStripEnv, strong.StripEnv);
        Assert.Equal(CommandClassNames.Read, strong.CommandClass);

        // GIT_CONFIG_KEY_0=credential.helper is -c credential.helper: secret-reveal, no auto-allow at Trusted.
        var ex = await Assert.ThrowsAsync<Grpc.Core.RpcException>(() => AgentAuthorizeClient.AuthorizeAsync(
            "git",
            new[] { "status" },
            pipeName: fx.PipeName,
            callerEnv: new Dictionary<string, string> { ["GIT_CONFIG_KEY_0"] = "credential.helper" }));
        Assert.Equal(Grpc.Core.StatusCode.PermissionDenied, ex.StatusCode);
        Assert.Contains("secret-reveal", ReadLatestAudit(fx.ProductRoot), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Strong_shim_removes_config_env_from_the_child()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await GitAuthFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.Terminal);
        var realGit = Path.Combine(fx.ProductRoot, "real-git.cmd");
        await File.WriteAllTextAsync(realGit, """
            @echo off
            if not "%GIT_CONFIG_GLOBAL%"=="" exit /b 7
            if not "%GIT_CONFIG_NOSYSTEM%"=="" exit /b 8
            exit /b 0
            """);
        new ToolPinStore(fx.ProductRoot).Save("git", realGit);
        new ToolPinStore(fx.ProductRoot).SetMode("git", ToolPin.StrongMode);

        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", Path.Combine(fx.ProductRoot, "elsewhere"));
        Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
        try
        {
            var exit = await GitShimApp.RunAsync(new[] { "status" }, pipeName: fx.PipeName, timeout: TimeSpan.FromSeconds(30));
            Assert.Equal(0, exit);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", null);
            Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", null);
        }
    }

    private static string ReadLatestAudit(string productRoot)
    {
        var auditDir = Path.Combine(productRoot, "audit");
        Assert.True(Directory.Exists(auditDir));
        var files = Directory.GetFiles(auditDir, "gates-*.ndjson");
        Assert.NotEmpty(files);
        return File.ReadAllText(files[0]);
    }



    private sealed class GitAuthFixture : IAsyncDisposable
    {
        public string PipeName { get; }
        public string ProductRoot { get; }
        public string PolicyPath { get; }
        public string SelectedPolicyKey { get; }
        private readonly AgentProcess _agent;

        private GitAuthFixture(
            string pipeName,
            string productRoot,
            string policyPath,
            string selectedPolicyKey,
            AgentProcess agent)
        {
            PipeName = pipeName;
            ProductRoot = productRoot;
            PolicyPath = policyPath;
            SelectedPolicyKey = selectedPolicyKey;
            _agent = agent;
        }

        public static async Task<GitAuthFixture?> CreateAsync(string approvalMode)
        {
            var pipeName = $"{AgentEndpoints.PipeNamePrefix}-gitauth-{Guid.NewGuid():N}";
            var productRoot = Path.Combine(Path.GetTempPath(), "cw-prod-" + Guid.NewGuid().ToString("N"));
            var policyPath = Path.Combine(productRoot, "policy.json");
            Directory.CreateDirectory(productRoot);
            var agentDll = TestPaths.FindAgentDll();

            Environment.SetEnvironmentVariable("CW_PIPE_NAME", pipeName);
            Environment.SetEnvironmentVariable("CW_POLICY_PATH", policyPath);
            Environment.SetEnvironmentVariable(ProductPaths.EnvVar, productRoot);

            var agent = await AgentProcess.StartAsync(agentDll, pipeName, policyPath, productRoot, approvalMode);
            var id = await AgentHealthClient.ResolveIdentityAsync(pipeName);
            if (!id.AutoApproveEligible)
            {
                await agent.DisposeAsync();
                Environment.SetEnvironmentVariable("CW_PIPE_NAME", null);
                Environment.SetEnvironmentVariable("CW_POLICY_PATH", null);
                Environment.SetEnvironmentVariable(ProductPaths.EnvVar, null);
                try { Directory.Delete(productRoot, recursive: true); } catch { /* ignore */ }
                return null;
            }

            return new GitAuthFixture(pipeName, productRoot, policyPath, id.SelectedPolicyKey, agent);
        }

        public void Enroll(LauncherEnrollmentKind kind)
        {
            var store = new PolicyStore(PolicyPath);
            store.Load();
            store.Enroll(SelectedPolicyKey, kind);
            store.Save();
        }

        public void PinCmdAsGit()
        {
            var pinTarget = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            new ToolPinStore(ProductRoot).Save("git", pinTarget);
        }

        public async ValueTask DisposeAsync()
        {
            await _agent.DisposeAsync();
            Environment.SetEnvironmentVariable("CW_PIPE_NAME", null);
            Environment.SetEnvironmentVariable("CW_POLICY_PATH", null);
            Environment.SetEnvironmentVariable(ProductPaths.EnvVar, null);
            try { Directory.Delete(ProductRoot, recursive: true); } catch { /* ignore */ }
        }
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
            string approvalMode = "off")
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
