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

    [Fact]
    public async Task Git_signs_a_commit_through_the_gate_and_the_signature_verifies()
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

        // #97: the steps of docs/use-cases/sign-commits-with-ssh.md, with -c in place of git config --global.
        var repo = Path.Combine(root, "repo");
        Directory.CreateDirectory(repo);
        var keygen = Path.Combine(OpenSshDir, "ssh-keygen.exe").Replace('\\', '/');
        var signers = Path.Combine(root, "allowed_signers");
        await File.WriteAllTextAsync(signers, "me@example.com " + upstream.PublicKeyLine + "\n");
        string[] config =
        [
            "-c", "user.name=Me", "-c", "user.email=me@example.com", "-c", "gpg.format=ssh",
            "-c", "gpg.ssh.program=" + keygen, "-c", "user.signingkey=key::" + upstream.PublicKeyLine,
            "-c", "gpg.ssh.allowedSignersFile=" + signers.Replace('\\', '/'),
        ];
        var (initExit, initOut) = await RunGitAsync(repo, root, "init", "-q");
        Assert.True(initExit == 0, initOut);

        var (commitExit, commitOut) = await RunGitAsync(repo, root, [.. config, "commit", "-q", "--allow-empty", "-S", "-m", "signed"]);
        Assert.True(commitExit == 0, commitOut);
        Assert.Equal(1, upstream.Signs);
        var (verifyExit, verifyOut) = await RunGitAsync(repo, root, [.. config, "verify-commit", "HEAD"]);
        Assert.True(verifyExit == 0, verifyOut);
        Assert.Contains("Good \"git\" signature for me@example.com", verifyOut, StringComparison.Ordinal);

        // An AI harness at Read level gets the card for a sign. With approval off, the commit fails.
        var store = new PolicyStore(agent.PolicyPath);
        store.Load();
        store.SetLevel(agent.SelectedPolicyKey, SshGate.Tool, PolicyLevel.Read);
        store.Save();
        var (deniedExit, deniedOut) = await RunGitAsync(repo, root, [.. config, "commit", "-q", "--allow-empty", "-S", "-m", "denied"]);
        Assert.NotEqual(0, deniedExit);
        Assert.Contains("agent refused operation", deniedOut, StringComparison.Ordinal);
        Assert.Equal(1, upstream.Signs);
    }

    private static async Task<(int Exit, string Output)> RunGitAsync(string repo, string home, params string[] args)
    {
        var psi = new ProcessStartInfo(CmdWarden.Cli.Harden.GitDiscoverer.FindRealGit() ?? "git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = repo };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        psi.Environment["SSH_AUTH_SOCK"] = SshGate.PipePath;
        psi.Environment["HOME"] = home;
        psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        psi.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(home, "gitconfig");
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(cts.Token);
        return (process.ExitCode, await stdout + await stderr);
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
