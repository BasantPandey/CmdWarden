using System.Diagnostics;
using System.Text;
using CmdWarden.Cli;
using CmdWarden.Cli.Harden;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// #37: a pack file alone gates a tool. The tool is a .cmd file, the pack is JSON in the product
/// root, and the shim is the one generic pack shim.
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class PackShimProcessTests
{
    private const string Pack = """
        {
          "tool": "sample",
          "binaries": ["sample.cmd"],
          "secretEnv": ["SAMPLE_TOKEN"],
          "rules": [
            { "match": "token show", "class": "secret-reveal" },
            { "match": "list", "class": "read" },
            { "match": "deploy", "class": "write", "risk": "high" },
            { "match": "run", "class": "write", "secrets": false }
          ],
          "samples": [ { "argv": "list", "class": "read" } ]
        }
        """;

    [Fact]
    public async Task A_test_pack_gates_a_sample_tool_with_no_new_code()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var agent = await TestAgent.StartAsync();
        if (agent is null)
            return;
        var root = agent.ProductRoot;
        Directory.CreateDirectory(ToolPacks.UserDir(root));
        await File.WriteAllTextAsync(Path.Combine(ToolPacks.UserDir(root), "sample.json"), Pack);
        var tool = Path.Combine(root, "bin", "sample.cmd");
        var output = Path.Combine(root, "out.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(tool)!);
        await File.WriteAllTextAsync(tool, $"""
            @echo off
            >"{output}" echo token=[%SAMPLE_TOKEN%] args=[%*]
            """);

        var pack = ToolPacks.Find("sample", root)!;
        var result = PackHarden.Run(pack, tool, skipUserPath: true, productRoot: root,
            shimSourceDir: TestPaths.FindShimOutputDir("CmdWarden.Shim.Pack", "cw-pack-shim"));
        Assert.True(File.Exists(result.ShimExePath));
        Assert.False(File.Exists(Path.Combine(root, "shims", "gh.exe")));
        Assert.False(File.Exists(Path.Combine(root, "shims", PackHarden.ShimExe)));

        agent.Enroll(LauncherEnrollmentKind.Terminal);
        var token = "sample_" + Guid.NewGuid().ToString("N");
        await AgentVaultClient.SaveAsync("SAMPLE_TOKEN", Encoding.UTF8.GetBytes(token), agent.PipeName);
        try
        {
            async Task<(int Exit, string Output)> Run(params string[] args)
            {
                File.Delete(output);
                var exit = await PackShimApp.RunAsync("sample", args, agent.PipeName, TimeSpan.FromSeconds(30));
                return (exit, File.Exists(output) ? await File.ReadAllTextAsync(output) : "");
            }

            // Read under Trusted: allowed, and the vault value reaches the child env only.
            var list = await Run("list");
            Assert.Equal(0, list.Exit);
            Assert.Contains($"token=[{token}]", list.Output, StringComparison.Ordinal);
            Assert.Null(Environment.GetEnvironmentVariable("SAMPLE_TOKEN"));

            // The rule says no secret.
            var run = await Run("run", "build");
            Assert.Equal(0, run.Exit);
            Assert.Contains("token=[]", run.Output, StringComparison.Ordinal);

            // secret-reveal under Trusted and a high-risk write both need the popup; approval is off.
            Assert.Equal(PackShimApp.ExitDenied, (await Run("token", "show")).Exit);
            Assert.Equal(PackShimApp.ExitDenied, (await Run("deploy", "app")).Exit);

            // cmd.exe must not run a second command, or expand a variable, from an argument.
            var inject = Path.Combine(root, "inject.txt");
            var amp = await Run("list", $"a&echo pwned>{inject}");
            Assert.Equal(0, amp.Exit);
            Assert.False(File.Exists(inject));
            Assert.Contains("a&echo pwned>", amp.Output, StringComparison.Ordinal);
            Assert.Equal(PackShimApp.ExitDenied, (await Run("list", "%SAMPLE_TOKEN%")).Exit);

            var audit = string.Join("\n", Directory.GetFiles(Path.Combine(root, "audit"), "gates-*.ndjson").Select(File.ReadAllText));
            Assert.Contains("\"tool\":\"sample\"", audit, StringComparison.Ordinal);
            Assert.Contains("SAMPLE_TOKEN", audit, StringComparison.Ordinal);
            Assert.DoesNotContain(token, audit, StringComparison.Ordinal);

            // The installed sample.exe is the pack shim under the tool name.
            var psi = new ProcessStartInfo(result.ShimExePath) { UseShellExecute = false, RedirectStandardError = true };
            psi.ArgumentList.Add("list");
            using var process = Process.Start(psi)!;
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, stderr);
            Assert.Contains($"token=[{token}]", await File.ReadAllTextAsync(output), StringComparison.Ordinal);
        }
        finally
        {
            try { await AgentVaultClient.DeleteAsync("SAMPLE_TOKEN", agent.PipeName); } catch { /* ignore */ }
        }
    }
}
