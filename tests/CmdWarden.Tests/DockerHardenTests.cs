using System.Diagnostics;
using System.Text;
using CmdWarden.Agent.Approval;
using CmdWarden.Cli;
using CmdWarden.Cli.Harden;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// cw harden docker + PATH shim (issue #46).
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class DockerHardenTests
{
    [Fact]
    public void Discoverer_skips_product_shims_dir()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-dock-disc-" + Guid.NewGuid().ToString("N"));
        var shims = Path.Combine(root, "shims");
        var realDir = Path.Combine(root, "real");
        Directory.CreateDirectory(shims);
        Directory.CreateDirectory(realDir);

        var shimDocker = Path.Combine(shims, "docker.exe");
        var realDocker = Path.Combine(realDir, "docker.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), shimDocker);
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), realDocker);

        var path = shims + Path.PathSeparator + realDir;
        var found = DockerDiscoverer.FindRealDocker(path, shims);
        Assert.NotNull(found);
        Assert.Equal(Path.GetFullPath(realDocker), Path.GetFullPath(found!), ignoreCase: true);
    }

    [Fact]
    public void Harden_pins_and_installs_shim_without_stripping_desktop_store()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var productRoot = Path.Combine(Path.GetTempPath(), "cw-prod-dockharden-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(productRoot);

        var realDir = Path.Combine(productRoot, "real");
        Directory.CreateDirectory(realDir);
        var realDocker = Path.Combine(realDir, "docker.cmd");
        File.WriteAllText(realDocker, """
            @echo off
            if /I not "%~1"=="pull" exit /b 8
            exit /b 0
            """);

        var shimSource = TestPaths.FindDockerShimOutputDir();
        try
        {
            var result = DockerHarden.Run(new DockerHardenOptions
            {
                RealDockerPath = realDocker,
                ProductRoot = productRoot,
                ShimSourceDir = shimSource,
                SkipUserPath = true,
            });

            Assert.Equal(Path.GetFullPath(realDocker), result.RealDockerPath, ignoreCase: true);
            Assert.True(File.Exists(result.ShimExePath));
            Assert.Contains("Desktop", result.CredentialNote, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("intact", result.CredentialNote, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("docker-credential", result.CredentialNote, StringComparison.OrdinalIgnoreCase);

            var pin = new ToolPinStore(productRoot).Check("docker");
            Assert.True(pin.IsOk);
            Assert.Equal(Path.GetFullPath(realDocker), pin.Pin!.Path, ignoreCase: true);
            Assert.True(File.Exists(Path.Combine(result.ShimsDir, "docker.exe")));
            Assert.True(File.Exists(Path.Combine(result.ShimsDir, HelperTools.DockerHelperExe)));
        }
        finally
        {
            try { Directory.Delete(productRoot, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Shim_grant_spawns_real_docker_parent_env_unchanged()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var pipeName = $"{AgentEndpoints.PipeNamePrefix}-dockshim-{Guid.NewGuid():N}";
        var productRoot = Path.Combine(Path.GetTempPath(), "cw-prod-" + Guid.NewGuid().ToString("N"));
        var policyPath = Path.Combine(productRoot, "policy.json");
        Directory.CreateDirectory(productRoot);

        var realDocker = Path.Combine(productRoot, "real-docker.cmd");
        await File.WriteAllTextAsync(realDocker, """
            @echo off
            if /I not "%~1"=="pull" exit /b 8
            exit /b 0
            """);

        var agentDll = TestPaths.FindAgentDll();
        var previousParentAuth = Environment.GetEnvironmentVariable("DOCKER_AUTH_CONFIG");
        Environment.SetEnvironmentVariable("DOCKER_AUTH_CONFIG", null);
        Environment.SetEnvironmentVariable("CW_PIPE_NAME", pipeName);
        Environment.SetEnvironmentVariable("CW_POLICY_PATH", policyPath);
        Environment.SetEnvironmentVariable(ProductPaths.EnvVar, productRoot);
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

            new ToolPinStore(productRoot).Save("docker", realDocker);

            var exit = await DockerShimApp.RunAsync(
                new[] { "pull", "alpine" },
                pipeName: pipeName,
                timeout: TimeSpan.FromSeconds(30));

            Assert.Equal(0, exit);
            Assert.Null(Environment.GetEnvironmentVariable("DOCKER_AUTH_CONFIG"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CW_PIPE_NAME", null);
            Environment.SetEnvironmentVariable("CW_POLICY_PATH", null);
            Environment.SetEnvironmentVariable(ProductPaths.EnvVar, null);
            Environment.SetEnvironmentVariable("DOCKER_AUTH_CONFIG", previousParentAuth);
            try { Directory.Delete(productRoot, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Shim_injects_child_DOCKER_AUTH_CONFIG_when_vaulted()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var pipeName = $"{AgentEndpoints.PipeNamePrefix}-dockauthshim-{Guid.NewGuid():N}";
        var productRoot = Path.Combine(Path.GetTempPath(), "cw-prod-" + Guid.NewGuid().ToString("N"));
        var policyPath = Path.Combine(productRoot, "policy.json");
        Directory.CreateDirectory(productRoot);

        var authJson = """{"auths":{"https://index.docker.io/v1/":{"auth":"dGVzdDpzZWNyZXQ="}}}""";
        var realDocker = Path.Combine(productRoot, "real-docker.cmd");
        // Use "if defined" so JSON quotes/braces in DOCKER_AUTH_CONFIG do not break cmd parsing.
        await File.WriteAllTextAsync(realDocker, """
            @echo off
            if not defined DOCKER_AUTH_CONFIG exit /b 9
            if /I not "%~1"=="pull" exit /b 8
            exit /b 0
            """);

        var agentDll = TestPaths.FindAgentDll();
        var previousParentAuth = Environment.GetEnvironmentVariable("DOCKER_AUTH_CONFIG");
        Environment.SetEnvironmentVariable("DOCKER_AUTH_CONFIG", null);
        Environment.SetEnvironmentVariable("CW_PIPE_NAME", pipeName);
        Environment.SetEnvironmentVariable("CW_POLICY_PATH", policyPath);
        Environment.SetEnvironmentVariable(ProductPaths.EnvVar, productRoot);
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

            new ToolPinStore(productRoot).Save("docker", realDocker);
            await AgentVaultClient.SaveAsync(
                SessionAgentServiceNames.DockerAuthConfig,
                Encoding.UTF8.GetBytes(authJson),
                pipeName);

            var exit = await DockerShimApp.RunAsync(
                new[] { "pull", "alpine" },
                pipeName: pipeName,
                timeout: TimeSpan.FromSeconds(30));

            Assert.Equal(0, exit);
            // Parent must not retain vaulted auth after shim returns
            Assert.NotEqual(authJson, Environment.GetEnvironmentVariable("DOCKER_AUTH_CONFIG"));
        }
        finally
        {
            try
            {
                await AgentVaultClient.DeleteAsync(SessionAgentServiceNames.DockerAuthConfig, pipeName);
            }
            catch { /* ignore */ }
            Environment.SetEnvironmentVariable("CW_PIPE_NAME", null);
            Environment.SetEnvironmentVariable("CW_POLICY_PATH", null);
            Environment.SetEnvironmentVariable(ProductPaths.EnvVar, null);
            Environment.SetEnvironmentVariable("DOCKER_AUTH_CONFIG", previousParentAuth);
            try { Directory.Delete(productRoot, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Shim_on_deny_exits_nonzero_without_spawning()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var pipeName = $"{AgentEndpoints.PipeNamePrefix}-dockshimdeny-{Guid.NewGuid():N}";
        var productRoot = Path.Combine(Path.GetTempPath(), "cw-prod-" + Guid.NewGuid().ToString("N"));
        var policyPath = Path.Combine(productRoot, "policy.json");
        Directory.CreateDirectory(productRoot);

        var realDocker = Path.Combine(productRoot, "real-docker.cmd");
        await File.WriteAllTextAsync(realDocker, """
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

            new ToolPinStore(productRoot).Save("docker", realDocker);

            var exit = await DockerShimApp.RunAsync(
                new[] { "pull", "alpine" },
                pipeName: pipeName,
                timeout: TimeSpan.FromSeconds(30));

            Assert.Equal(DockerShimApp.ExitDenied, exit);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CW_PIPE_NAME", null);
            Environment.SetEnvironmentVariable("CW_POLICY_PATH", null);
            Environment.SetEnvironmentVariable(ProductPaths.EnvVar, null);
            try { Directory.Delete(productRoot, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Shim_agent_down_does_not_fall_through()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var noAgent = new NoAgentBinary();
        var missingPipe = $"{AgentEndpoints.PipeNamePrefix}-missing-dock-{Guid.NewGuid():N}";
        var exit = await DockerShimApp.RunAsync(
            new[] { "pull", "alpine" },
            pipeName: missingPipe,
            timeout: TimeSpan.FromMilliseconds(800));

        Assert.Equal(DockerShimApp.ExitAgentDown, exit);
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
