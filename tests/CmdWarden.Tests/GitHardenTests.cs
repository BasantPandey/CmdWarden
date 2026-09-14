using System.Diagnostics;
using CmdWarden.Agent.Approval;
using CmdWarden.Cli;
using CmdWarden.Cli.Harden;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// cw harden git + PATH shim (issue #44).
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class GitHardenTests
{
    [Fact]
    public void Discoverer_skips_product_shims_dir()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-git-disc-" + Guid.NewGuid().ToString("N"));
        var shims = Path.Combine(root, "shims");
        var realDir = Path.Combine(root, "real");
        Directory.CreateDirectory(shims);
        Directory.CreateDirectory(realDir);

        var shimGit = Path.Combine(shims, "git.exe");
        var realGit = Path.Combine(realDir, "git.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), shimGit);
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), realGit);

        var path = shims + Path.PathSeparator + realDir;
        var found = GitDiscoverer.FindRealGit(path, shims);
        Assert.NotNull(found);
        Assert.Equal(Path.GetFullPath(realGit), Path.GetFullPath(found!), ignoreCase: true);
    }

    [Fact]
    public void Harden_pins_and_installs_shim_without_migrating_credentials()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var productRoot = Path.Combine(Path.GetTempPath(), "cw-prod-githarden-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(productRoot);

        var realDir = Path.Combine(productRoot, "real");
        Directory.CreateDirectory(realDir);
        var realGit = Path.Combine(realDir, "git.cmd");
        File.WriteAllText(realGit, """
            @echo off
            if /I not "%~1"=="status" exit /b 8
            exit /b 0
            """);

        var shimSource = TestPaths.FindGitShimOutputDir();
        try
        {
            var result = GitHarden.Run(new GitHardenOptions
            {
                RealGitPath = realGit,
                ProductRoot = productRoot,
                ShimSourceDir = shimSource,
                HelperSourceDir = TestPaths.FindGitHelperOutputDir(),
                SkipUserPath = true,
            });

            Assert.Equal(Path.GetFullPath(realGit), result.RealGitPath, ignoreCase: true);
            Assert.True(File.Exists(result.ShimExePath));
            Assert.Contains("GCM", result.CredentialNote, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("intact", result.CredentialNote, StringComparison.OrdinalIgnoreCase);

            var pin = new ToolPinStore(productRoot).Check("git");
            Assert.True(pin.IsOk);
            Assert.Equal(Path.GetFullPath(realGit), pin.Pin!.Path, ignoreCase: true);
            Assert.True(File.Exists(Path.Combine(result.ShimsDir, "git.exe")));
            Assert.Equal(
                Path.Combine(result.ShimsDir, HelperTools.GitHelperExe),
                result.HelperExePath,
                ignoreCase: true);
            Assert.True(File.Exists(result.HelperExePath));
        }
        finally
        {
            try { Directory.Delete(productRoot, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Shim_grant_spawns_real_git_without_secret_env()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var pipeName = $"{AgentEndpoints.PipeNamePrefix}-gitshim-{Guid.NewGuid():N}";
        var productRoot = Path.Combine(Path.GetTempPath(), "cw-prod-" + Guid.NewGuid().ToString("N"));
        var policyPath = Path.Combine(productRoot, "policy.json");
        Directory.CreateDirectory(productRoot);

        var realGit = Path.Combine(productRoot, "real-git.cmd");
        await File.WriteAllTextAsync(realGit, """
            @echo off
            if not "%DOCKER_AUTH_CONFIG%"=="" exit /b 6
            if not "%GH_TOKEN%"=="" exit /b 7
            if /I not "%~1"=="status" exit /b 8
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

            new ToolPinStore(productRoot).Save("git", realGit);

            var exit = await GitShimApp.RunAsync(
                new[] { "status" },
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

        var pipeName = $"{AgentEndpoints.PipeNamePrefix}-gitshimdeny-{Guid.NewGuid():N}";
        var productRoot = Path.Combine(Path.GetTempPath(), "cw-prod-" + Guid.NewGuid().ToString("N"));
        var policyPath = Path.Combine(productRoot, "policy.json");
        Directory.CreateDirectory(productRoot);

        var realGit = Path.Combine(productRoot, "real-git.cmd");
        await File.WriteAllTextAsync(realGit, """
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

            // No enrollment → deny under approval off
            new ToolPinStore(productRoot).Save("git", realGit);

            var exit = await GitShimApp.RunAsync(
                new[] { "status" },
                pipeName: pipeName,
                timeout: TimeSpan.FromSeconds(30));

            Assert.Equal(GitShimApp.ExitDenied, exit);
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
        var missingPipe = $"{AgentEndpoints.PipeNamePrefix}-missing-git-{Guid.NewGuid():N}";
        var exit = await GitShimApp.RunAsync(
            new[] { "status" },
            pipeName: missingPipe,
            timeout: TimeSpan.FromMilliseconds(800));

        Assert.Equal(GitShimApp.ExitAgentDown, exit);
    }

    [Fact]
    public void SpawnReal_uses_absolute_path_only()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var exit = GitShimApp.SpawnReal(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            new[] { "/c", "exit 0" },
            new Dictionary<string, string>());

        Assert.Equal(0, exit);
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
