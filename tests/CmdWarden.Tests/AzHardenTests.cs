using System.Diagnostics;
using CmdWarden.Agent.Approval;
using CmdWarden.Cli;
using CmdWarden.Cli.Harden;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// cw harden az + PATH shim (issue #45).
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class AzHardenTests
{
    [Fact]
    public void Discoverer_prefers_az_cmd_and_skips_product_shims()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-az-disc-" + Guid.NewGuid().ToString("N"));
        var shims = Path.Combine(root, "shims");
        var realDir = Path.Combine(root, "real");
        Directory.CreateDirectory(shims);
        Directory.CreateDirectory(realDir);

        var shimAz = Path.Combine(shims, "az.exe");
        var realAzCmd = Path.Combine(realDir, "az.cmd");
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), shimAz);
        File.WriteAllText(realAzCmd, "@echo off\r\nexit /b 0\r\n");

        var path = shims + Path.PathSeparator + realDir;
        var found = AzDiscoverer.FindRealAz(path, shims);
        Assert.NotNull(found);
        Assert.Equal(Path.GetFullPath(realAzCmd), Path.GetFullPath(found!), ignoreCase: true);
    }

    [Fact]
    public void Discoverer_falls_back_to_az_exe()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-az-exe-" + Guid.NewGuid().ToString("N"));
        var realDir = Path.Combine(root, "real");
        Directory.CreateDirectory(realDir);
        var realAz = Path.Combine(realDir, "az.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), realAz);

        try
        {
            var found = AzDiscoverer.FindRealAz(realDir, Path.Combine(root, "shims"));
            Assert.NotNull(found);
            Assert.Equal(Path.GetFullPath(realAz), Path.GetFullPath(found!), ignoreCase: true);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Harden_pins_and_installs_shim_without_migrating_msal()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var productRoot = Path.Combine(Path.GetTempPath(), "cw-prod-azharden-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(productRoot);

        var realDir = Path.Combine(productRoot, "real");
        Directory.CreateDirectory(realDir);
        var realAz = Path.Combine(realDir, "az.cmd");
        File.WriteAllText(realAz, """
            @echo off
            if /I not "%~1"=="account" exit /b 8
            exit /b 0
            """);

        var shimSource = TestPaths.FindAzShimOutputDir();
        try
        {
            var result = AzHarden.Run(new AzHardenOptions
            {
                RealAzPath = realAz,
                ProductRoot = productRoot,
                ShimSourceDir = shimSource,
                SkipUserPath = true,
            });

            Assert.Equal(Path.GetFullPath(realAz), result.RealAzPath, ignoreCase: true);
            Assert.True(File.Exists(result.ShimExePath));
            Assert.Contains("MSAL", result.CredentialNote, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("intact", result.CredentialNote, StringComparison.OrdinalIgnoreCase);

            var pin = new ToolPinStore(productRoot).Check("az");
            Assert.True(pin.IsOk);
            Assert.Equal(Path.GetFullPath(realAz), pin.Pin!.Path, ignoreCase: true);
            Assert.True(File.Exists(Path.Combine(result.ShimsDir, "az.exe")));
        }
        finally
        {
            try { Directory.Delete(productRoot, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Shim_grant_spawns_real_az_without_secret_env()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var pipeName = $"{AgentEndpoints.PipeNamePrefix}-azshim-{Guid.NewGuid():N}";
        var productRoot = Path.Combine(Path.GetTempPath(), "cw-prod-" + Guid.NewGuid().ToString("N"));
        var policyPath = Path.Combine(productRoot, "policy.json");
        Directory.CreateDirectory(productRoot);

        var realAz = Path.Combine(productRoot, "real-az.cmd");
        await File.WriteAllTextAsync(realAz, """
            @echo off
            if not "%AZURE_CLIENT_SECRET%"=="" exit /b 6
            if not "%GH_TOKEN%"=="" exit /b 7
            if /I not "%~1"=="account" exit /b 8
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
            Assert.True(id.AutoApproveEligible);

            var store = new PolicyStore(policyPath);
            store.Load();
            store.Enroll(id.SelectedPolicyKey, LauncherEnrollmentKind.Terminal);
            store.Save();

            new ToolPinStore(productRoot).Save("az", realAz);

            var exit = await AzShimApp.RunAsync(
                new[] { "account", "list" },
                pipeName: pipeName,
                timeout: TimeSpan.FromSeconds(30));

            Assert.Equal(0, exit);
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
    public async Task Shim_on_deny_exits_nonzero_without_spawning()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var pipeName = $"{AgentEndpoints.PipeNamePrefix}-azshimdeny-{Guid.NewGuid():N}";
        var productRoot = Path.Combine(Path.GetTempPath(), "cw-prod-" + Guid.NewGuid().ToString("N"));
        var policyPath = Path.Combine(productRoot, "policy.json");
        Directory.CreateDirectory(productRoot);

        var realAz = Path.Combine(productRoot, "real-az.cmd");
        await File.WriteAllTextAsync(realAz, """
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

            new ToolPinStore(productRoot).Save("az", realAz);

            var exit = await AzShimApp.RunAsync(
                new[] { "account", "list" },
                pipeName: pipeName,
                timeout: TimeSpan.FromSeconds(30));

            Assert.Equal(AzShimApp.ExitDenied, exit);
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
        var missingPipe = $"{AgentEndpoints.PipeNamePrefix}-missing-az-{Guid.NewGuid():N}";
        var exit = await AzShimApp.RunAsync(
            new[] { "account", "list" },
            pipeName: missingPipe,
            timeout: TimeSpan.FromMilliseconds(800));

        Assert.Equal(AzShimApp.ExitAgentDown, exit);
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
