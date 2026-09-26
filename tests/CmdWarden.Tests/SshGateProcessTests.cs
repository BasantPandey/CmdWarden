using System.Diagnostics;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Ssh;

namespace CmdWarden.Tests;

/// <summary>
/// #39: the Windows OpenSSH tools talk to the ssh gate pipe of a real Session Agent. The gate
/// forwards to a fake real agent. A sign that policy allows reaches the real agent; a sign that
/// needs the popup (approval off in tests) gets SSH_AGENT_FAILURE and never reaches it.
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class SshGateProcessTests
{
    private static readonly string OpenSshDir = Path.Combine(Environment.SystemDirectory, "OpenSSH");

    [Fact]
    public async Task A_sign_runs_only_when_policy_allows_it()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(Path.Combine(OpenSshDir, "ssh-keygen.exe")))
            return;

        await using var upstream = new FakeSshAgent();
        var root = Path.Combine(Path.GetTempPath(), "cw-prod-" + Guid.NewGuid().ToString("N"));
        SshGate.Save(new SshGateState { Upstream = upstream.PipePath }, root);
        await using var agent = await TestAgent.StartAsync(productRoot: root);
        if (agent is null)
            return;
        agent.Enroll(LauncherEnrollmentKind.Terminal);
        await WaitForPipeAsync(SshGate.PipePath);

        // ssh-add -l lists the key of the real agent through the gate.
        var (listExit, list) = await RunAsync("ssh-add.exe", root, "-l");
        Assert.Equal(0, listExit);
        Assert.Contains(upstream.Comment, list, StringComparison.Ordinal);

        // Commit signing (ssh-keygen -Y sign) is write: Trusted runs it with no popup.
        var pub = Path.Combine(root, "key.pub");
        var data = Path.Combine(root, "data.txt");
        await File.WriteAllTextAsync(pub, upstream.PublicKeyLine + "\n");
        await File.WriteAllTextAsync(data, "hello\n");
        var (signExit, signOut) = await RunAsync("ssh-keygen.exe", root, "-Y", "sign", "-f", pub, "-n", "git", data);
        Assert.True(signExit == 0, signOut);
        Assert.True(File.Exists(data + ".sig"));
        Assert.Equal(1, upstream.Signs);

        // Read level: a write sign needs the popup. With approval off, the gate refuses it.
        var store = new PolicyStore(agent.PolicyPath);
        store.Load();
        store.SetLevel(agent.SelectedPolicyKey, SshGate.Tool, PolicyLevel.Read);
        store.Save();
        File.Delete(data + ".sig");
        var (deniedExit, _) = await RunAsync("ssh-keygen.exe", root, "-Y", "sign", "-f", pub, "-n", "git", data);
        Assert.NotEqual(0, deniedExit);
        Assert.Equal(1, upstream.Signs);

        var audit = string.Join("\n", Directory.GetFiles(Path.Combine(root, "audit"), "gates-*.ndjson").Select(File.ReadAllText));
        Assert.Contains("\"tool\":\"ssh\"", audit, StringComparison.Ordinal);
        Assert.Contains(upstream.Comment, audit, StringComparison.Ordinal);
        Assert.Contains("\"purpose\":\"sign\"", audit, StringComparison.Ordinal);
    }

    private static async Task<(int Exit, string Output)> RunAsync(string exe, string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo(Path.Combine(OpenSshDir, exe))
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        psi.Environment["SSH_AUTH_SOCK"] = SshGate.PipePath;
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(cts.Token);
        return (process.ExitCode, await stdout + await stderr);
    }

    private static async Task WaitForPipeAsync(string pipePath)
    {
        for (var i = 0; i < 100 && !SshGate.PipeExists(pipePath); i++)
            await Task.Delay(100);
        Assert.True(SshGate.PipeExists(pipePath), $"ssh gate pipe {pipePath} did not start");
    }
}
