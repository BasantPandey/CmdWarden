using System.Diagnostics;
using System.IO.Compression;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Structural checks for PackAsTool layout (issue #37).
/// </summary>
public class DotnetToolPackageTests
{
    [Fact]
    public void Cli_csproj_is_pack_as_tool_with_stable_ids()
    {
        var csproj = Path.Combine(TestPaths.RepoRoot, "src", "CmdWarden.Cli", "CmdWarden.Cli.csproj");
        Assert.True(File.Exists(csproj));
        var xml = File.ReadAllText(csproj);
        Assert.Contains("<PackAsTool>true</PackAsTool>", xml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<ToolCommandName>cw</ToolCommandName>", xml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<PackageId>CmdWarden</PackageId>", xml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"<Version>{ProductInfo.DefaultVersion}</Version>", xml, StringComparison.Ordinal);
        Assert.Contains("CopyAgentPayload", xml, StringComparison.Ordinal);
        Assert.Contains("CopyApprovalGatePayload", xml, StringComparison.Ordinal);
        Assert.Contains("CopySecretsManagerPayload", xml, StringComparison.Ordinal);
        Assert.Contains("IncludePayloadsInPublish", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Agent_and_Cli_share_product_version()
    {
        // CI builds with -p:Version=<tag>, so ProductInfo.Version differs from DefaultVersion there.
        var expected = $"<Version>{ProductInfo.DefaultVersion}</Version>";
        var agentCsproj = File.ReadAllText(
            Path.Combine(TestPaths.RepoRoot, "src", "CmdWarden.Agent", "CmdWarden.Agent.csproj"));
        Assert.Contains(expected, agentCsproj, StringComparison.Ordinal);
        var cliCsproj = File.ReadAllText(
            Path.Combine(TestPaths.RepoRoot, "src", "CmdWarden.Cli", "CmdWarden.Cli.csproj"));
        Assert.Contains(expected, cliCsproj, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pack_nupkg_contains_lg_and_agent_payload()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // Pack Release so outputs do not collide with Debug assemblies held by the test host.
        var root = TestPaths.RepoRoot;
        var outDir = Path.Combine(Path.GetTempPath(), "cw-nupkg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("pack");
            psi.ArgumentList.Add(Path.Combine(root, "src", "CmdWarden.Cli", "CmdWarden.Cli.csproj"));
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("Release");
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(outDir);
            psi.ArgumentList.Add("-m:1");

            using var p = Process.Start(psi) ?? throw new InvalidOperationException("dotnet pack failed to start");
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(180));
            try
            {
                await p.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
                Assert.Fail("dotnet pack timed out after 180s");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            Assert.True(p.ExitCode == 0, $"dotnet pack failed: {stdout}\n{stderr}");

            var nupkg = Directory.GetFiles(outDir, "CmdWarden.*.nupkg").FirstOrDefault();
            Assert.NotNull(nupkg);

            using var zip = ZipFile.OpenRead(nupkg!);
            var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();

            Assert.Contains(names, n => n.Contains("/cw.dll", StringComparison.OrdinalIgnoreCase)
                                        || n.EndsWith("cw.dll", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(names, n => n.Contains("/agent/", StringComparison.OrdinalIgnoreCase)
                                        && n.EndsWith("CmdWarden.Agent.dll", StringComparison.OrdinalIgnoreCase));
            // Approval Gate helper under agent/approval-gate/ (ticket #81).
            Assert.Contains(names, n => n.Contains("/agent/approval-gate/", StringComparison.OrdinalIgnoreCase)
                                        && n.EndsWith("CmdWarden.ApprovalGate.exe", StringComparison.OrdinalIgnoreCase));
            // Vault UI under secrets-manager/ at tool root (ticket #96).
            Assert.Contains(names, n => n.Contains("/secrets-manager/", StringComparison.OrdinalIgnoreCase)
                                        && n.EndsWith("CmdWarden.SecretsManager.exe", StringComparison.OrdinalIgnoreCase));
            // Install is binaries-only: package must not encode user PATH shim mutation scripts.
            Assert.DoesNotContain(names, n => n.Contains("harden-path", StringComparison.OrdinalIgnoreCase));
            // gh shim must live under shim-payload/, not as a root tool command.
            Assert.DoesNotContain(names, n => n.EndsWith("/any/gh.exe", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(outDir, recursive: true); } catch { /* ignore */ }
        }
    }

}
