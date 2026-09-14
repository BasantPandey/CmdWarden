using System.Diagnostics;
using System.Text;
using CmdWarden.Agent.Approval;
using CmdWarden.Cli;
using CmdWarden.Cli.Harden;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// cw harden gh (issue #33).
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class GhHardenTests
{
    [Fact]
    public void Discoverer_skips_product_shims_dir()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-disc-" + Guid.NewGuid().ToString("N"));
        var shims = Path.Combine(root, "shims");
        var realDir = Path.Combine(root, "real");
        Directory.CreateDirectory(shims);
        Directory.CreateDirectory(realDir);

        var shimGh = Path.Combine(shims, "gh.exe");
        var realGh = Path.Combine(realDir, "gh.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), shimGh);
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), realGh);

        var path = shims + Path.PathSeparator + realDir;
        var found = GhDiscoverer.FindRealGh(path, shims);
        Assert.NotNull(found);
        Assert.Equal(Path.GetFullPath(realGh), Path.GetFullPath(found!), ignoreCase: true);
    }

    [Fact]
    public async Task Harden_pins_installs_shim_imports_token_without_touching_parent_path_when_skipped()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var pipeName = $"{AgentEndpoints.PipeNamePrefix}-harden-{Guid.NewGuid():N}";
        var productRoot = Path.Combine(Path.GetTempPath(), "cw-prod-" + Guid.NewGuid().ToString("N"));
        var policyPath = Path.Combine(productRoot, "policy.json");
        Directory.CreateDirectory(productRoot);

        var realDir = Path.Combine(productRoot, "real");
        Directory.CreateDirectory(realDir);
        var realGh = Path.Combine(realDir, "gh.cmd");
        await File.WriteAllTextAsync(realGh, """
            @echo off
            if "%GH_TOKEN%"=="" exit /b 7
            if /I not "%~1"=="pr" exit /b 8
            exit /b 0
            """);

        var shimSource = TestPaths.FindGhShimOutputDir();
        var agentDll = TestPaths.FindAgentDll();
        var token = "import-" + Guid.NewGuid().ToString("N");

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

            var result = await GhHarden.RunAsync(new GhHardenOptions
            {
                RealGhPath = realGh,
                ProductRoot = productRoot,
                ShimSourceDir = shimSource,
                TokenOverride = token,
                SkipUserPath = true,
                PipeName = pipeName,
            });

            Assert.Equal(Path.GetFullPath(realGh), result.RealGhPath, ignoreCase: true);
            Assert.True(File.Exists(result.ShimExePath));
            Assert.True(result.TokenImported);

            var pin = new ToolPinStore(productRoot).Check("gh");
            Assert.True(pin.IsOk);
            Assert.Equal(Path.GetFullPath(realGh), pin.Pin!.Path, ignoreCase: true);

            var released = await AgentVaultClient.ReleaseAsync(
                "GH_TOKEN", "test", pipeName, tool: "inject", commandClass: CommandClassNames.Write);
            var raw = released.Value.ToByteArray();
            Assert.Equal(token, Encoding.UTF8.GetString(raw));
            Array.Clear(raw);

            // Journey: shim Authorize → spawn pinned real-gh.cmd with child token
            var exit = await GhShimApp.RunAsync(
                new[] { "pr", "list" },
                pipeName: pipeName,
                timeout: TimeSpan.FromSeconds(30));
            Assert.Equal(0, exit);
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
