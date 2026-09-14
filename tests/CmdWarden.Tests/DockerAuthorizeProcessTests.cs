using System.Diagnostics;
using System.Text;
using CmdWarden.Agent.Approval;
using CmdWarden.Cli;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Primary seam: Authorize for tool=docker (issue #42).
/// Gate-only when vault auth absent; optional child-only DOCKER_AUTH_CONFIG when present.
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class DockerAuthorizeProcessTests
{
    [Fact]
    public async Task Authorize_docker_pull_auto_allows_Trusted_without_secret_map()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await DockerAuthFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.Terminal);
        fx.PinCmdAsDocker();

        var grant = await AgentAuthorizeClient.AuthorizeAsync(
            "docker",
            new[] { "pull", "alpine" },
            pipeName: fx.PipeName);

        Assert.True(grant.Allowed);
        Assert.Equal("docker", grant.Tool);
        Assert.Equal(CommandClassNames.Read, grant.CommandClass);
        Assert.Equal(PolicyLevelNames.Trusted, grant.PolicyLevel);
        Assert.Equal("auto-allow", grant.Decision);
        Assert.False(string.IsNullOrWhiteSpace(grant.RealPath));
        Assert.Empty(grant.Env);

        var audit = ReadLatestAudit(fx.ProductRoot);
        Assert.Contains("auto-allow", audit, StringComparison.Ordinal);
        Assert.Contains("\"tool\":\"docker\"", audit, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"commandClass\":\"read\"", audit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", audit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("auth\":", audit, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Authorize_docker_push_write_auto_allows_Trusted_without_secret_map()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await DockerAuthFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.Terminal);
        fx.PinCmdAsDocker();

        var grant = await AgentAuthorizeClient.AuthorizeAsync(
            "docker",
            new[] { "push", "myimage:latest" },
            pipeName: fx.PipeName);

        Assert.True(grant.Allowed);
        Assert.Equal(CommandClassNames.Write, grant.CommandClass);
        Assert.Equal("auto-allow", grant.Decision);
        Assert.Empty(grant.Env);
    }

    [Fact]
    public async Task Authorize_docker_login_write_auto_allows_Trusted()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await DockerAuthFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.Terminal);
        fx.PinCmdAsDocker();

        var grant = await AgentAuthorizeClient.AuthorizeAsync(
            "docker",
            new[] { "login" },
            pipeName: fx.PipeName);

        Assert.True(grant.Allowed);
        Assert.Equal(CommandClassNames.Write, grant.CommandClass);
        Assert.Empty(grant.Env);
    }

    [Fact]
    public async Task Authorize_docker_unknown_not_auto_allowed_under_Trusted()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await DockerAuthFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.Terminal);
        fx.PinCmdAsDocker();

        var ex = await Assert.ThrowsAsync<Grpc.Core.RpcException>(async () =>
            await AgentAuthorizeClient.AuthorizeAsync(
                "docker",
                new[] { "totally-unknown-subcommand" },
                pipeName: fx.PipeName));

        Assert.Equal(Grpc.Core.StatusCode.PermissionDenied, ex.StatusCode);
    }

    [Fact]
    public async Task Authorize_docker_write_not_auto_allowed_under_Read()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await DockerAuthFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.AiHarness);
        fx.PinCmdAsDocker();

        var ex = await Assert.ThrowsAsync<Grpc.Core.RpcException>(async () =>
            await AgentAuthorizeClient.AuthorizeAsync(
                "docker",
                new[] { "push", "img" },
                pipeName: fx.PipeName));

        Assert.Equal(Grpc.Core.StatusCode.PermissionDenied, ex.StatusCode);
    }

    [Fact]
    public async Task Authorize_docker_denies_when_pin_missing()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await DockerAuthFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.Terminal);

        var ex = await Assert.ThrowsAsync<Grpc.Core.RpcException>(async () =>
            await AgentAuthorizeClient.AuthorizeAsync(
                "docker",
                new[] { "pull", "alpine" },
                pipeName: fx.PipeName));

        Assert.Equal(Grpc.Core.StatusCode.FailedPrecondition, ex.StatusCode);
        Assert.Contains(PolicyReasonCodes.PinMissing, ex.Status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authorize_docker_denies_when_pin_hash_mismatches()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await DockerAuthFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.Terminal);
        var pinTarget = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        new ToolPinStore(fx.ProductRoot).Save("docker", pinTarget, sha256Hex: new string('c', 64));

        var ex = await Assert.ThrowsAsync<Grpc.Core.RpcException>(async () =>
            await AgentAuthorizeClient.AuthorizeAsync(
                "docker",
                new[] { "pull", "alpine" },
                pipeName: fx.PipeName));

        Assert.Equal(Grpc.Core.StatusCode.FailedPrecondition, ex.StatusCode);
        Assert.Contains(PolicyReasonCodes.PinMismatch, ex.Status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authorize_docker_help_grants_without_env()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await DockerAuthFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.Terminal);
        fx.PinCmdAsDocker();

        var grant = await AgentAuthorizeClient.AuthorizeAsync(
            "docker",
            new[] { "push", "--help" },
            pipeName: fx.PipeName);

        Assert.True(grant.Allowed);
        Assert.Equal(CommandClassNames.Read, grant.CommandClass);
        Assert.Empty(grant.Env);
    }

    [Fact]
    public async Task Authorize_docker_injects_child_DOCKER_AUTH_CONFIG_when_vaulted()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await DockerAuthFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.Terminal);
        fx.PinCmdAsDocker();

        // Minimal JSON; value must not appear in audit.
        var authJson = """{"auths":{"https://index.docker.io/v1/":{"auth":"dGVzdDpzZWNyZXQ="}}}""";
        await AgentVaultClient.SaveAsync(
            SessionAgentServiceNames.DockerAuthConfig,
            Encoding.UTF8.GetBytes(authJson),
            fx.PipeName);

        try
        {
            var previousParent = Environment.GetEnvironmentVariable("DOCKER_AUTH_CONFIG");
            Environment.SetEnvironmentVariable("DOCKER_AUTH_CONFIG", null);
            try
            {
                var grant = await AgentAuthorizeClient.AuthorizeAsync(
                    "docker",
                    new[] { "pull", "alpine" },
                    pipeName: fx.PipeName);

                Assert.True(grant.Allowed);
                Assert.True(grant.Env.ContainsKey(SessionAgentServiceNames.DockerAuthConfig));
                Assert.Equal(authJson, grant.Env[SessionAgentServiceNames.DockerAuthConfig]);
                // Parent process env unchanged (child-only overlay on Grant)
                Assert.NotEqual(authJson, Environment.GetEnvironmentVariable("DOCKER_AUTH_CONFIG"));

                var audit = ReadLatestAudit(fx.ProductRoot);
                Assert.Contains("DOCKER_AUTH_CONFIG", audit, StringComparison.Ordinal);
                Assert.DoesNotContain("dGVzdDpzZWNyZXQ=", audit, StringComparison.Ordinal);
                Assert.DoesNotContain(authJson, audit, StringComparison.Ordinal);
            }
            finally
            {
                Environment.SetEnvironmentVariable("DOCKER_AUTH_CONFIG", previousParent);
            }
        }
        finally
        {
            try
            {
                await AgentVaultClient.DeleteAsync(SessionAgentServiceNames.DockerAuthConfig, fx.PipeName);
            }
            catch { /* ignore */ }
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



    private sealed class DockerAuthFixture : IAsyncDisposable
    {
        public string PipeName { get; }
        public string ProductRoot { get; }
        public string PolicyPath { get; }
        public string SelectedPolicyKey { get; }
        private readonly AgentProcess _agent;

        private DockerAuthFixture(
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

        public static async Task<DockerAuthFixture?> CreateAsync(string approvalMode)
        {
            var pipeName = $"{AgentEndpoints.PipeNamePrefix}-dockauth-{Guid.NewGuid():N}";
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

            // Vault targets are user-global; clear residual DOCKER_AUTH_CONFIG from other tests.
            try
            {
                await AgentVaultClient.DeleteAsync(SessionAgentServiceNames.DockerAuthConfig, pipeName);
            }
            catch { /* ignore */ }

            return new DockerAuthFixture(pipeName, productRoot, policyPath, id.SelectedPolicyKey, agent);
        }

        public void Enroll(LauncherEnrollmentKind kind)
        {
            var store = new PolicyStore(PolicyPath);
            store.Load();
            store.Enroll(SelectedPolicyKey, kind);
            store.Save();
        }

        public void PinCmdAsDocker()
        {
            var pinTarget = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            new ToolPinStore(ProductRoot).Save("docker", pinTarget);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await AgentVaultClient.DeleteAsync(SessionAgentServiceNames.DockerAuthConfig, PipeName);
            }
            catch { /* ignore */ }
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
