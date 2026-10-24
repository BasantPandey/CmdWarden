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
}
