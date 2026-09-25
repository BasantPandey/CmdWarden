using System.Diagnostics;
using System.Text;
using CmdWarden.Agent.Identity;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// #31 end to end: the real gh shim runs under a real pwsh. The pwsh launcher is enrolled as a
/// Trusted terminal, so gh pr list auto-allows. The same call inside -EncodedCommand forces the
/// Approval Gate, which the scripted gate denies.
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class PowerShellWrapperProcessTests
{
    private static readonly string Pwsh = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");

    [Fact]
    public async Task Encoded_pwsh_forces_the_gate_and_plain_pwsh_keeps_its_policy()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(Pwsh))
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync("deny", enroll: null);
        if (fx is null)
            return;

        // The shim sits in the product shims dir, so the launcher is the pwsh above it.
        var shims = Path.Combine(fx.ProductRoot, "shims");
        Directory.CreateDirectory(shims);
        foreach (var file in Directory.GetFiles(TestPaths.FindGhShimOutputDir()))
            File.Copy(file, Path.Combine(shims, Path.GetFileName(file)), overwrite: true);
        var shim = Path.Combine(shims, "gh.exe");
        var realGh = Path.Combine(fx.ProductRoot, "real-gh.cmd");
        await File.WriteAllTextAsync(realGh, "@echo off\r\necho real-gh-ran\r\nexit /b 0\r\n");
        new ToolPinStore(fx.ProductRoot).Save("gh", realGh);
        await fx.SaveTokenAsync("wrapper-" + Guid.NewGuid().ToString("N"));

        var pwshNode = new ProcessNode { Pid = 0, ParentPid = 0, Path = Pwsh, FileName = "pwsh.exe" };
        ImageIdentity.Populate(pwshNode);
        var store = new PolicyStore(fx.PolicyPath);
        store.Load();
        store.Enroll(pwshNode.PolicyKey, LauncherEnrollmentKind.Terminal);
        store.Save();

        var script = $"& '{shim}' pr list; exit $LASTEXITCODE";
        var plain = await RunPwshAsync(fx, "-NoProfile", "-Command", script);
        Assert.Equal(0, plain.Exit);
        Assert.Contains("real-gh-ran", plain.Output);

        var encoded = await RunPwshAsync(fx, "-NoProfile", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        Assert.Equal(GhShimApp.ExitDenied, encoded.Exit);
        Assert.DoesNotContain("real-gh-ran", encoded.Output);

        var lines = fx.AuditLines();
        Assert.Contains(lines, l => l.Contains("\"decision\":\"auto-allow\"", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("\"decision\":\"deny\"", StringComparison.Ordinal)
            && l.Contains(PolicyReasonCodes.HiddenCommand, StringComparison.Ordinal));
    }

    private static async Task<(int Exit, string Output)> RunPwshAsync(ApprovalMemoryFixture fx, params string[] args)
    {
        var psi = new ProcessStartInfo(Pwsh)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        psi.Environment["CW_PIPE_NAME"] = fx.PipeName;
        psi.Environment["CW_POLICY_PATH"] = fx.PolicyPath;
        psi.Environment[ProductPaths.EnvVar] = fx.ProductRoot;
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdout + await stderr);
    }
}
