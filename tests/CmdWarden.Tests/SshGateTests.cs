using CmdWarden.Contracts;
using CmdWarden.Contracts.Ssh;

namespace CmdWarden.Tests;

public class SshGateTests
{
    [Theory]
    [InlineData("ssh -o SendEnv=GIT_PROTOCOL git@github.com \"git-upload-pack 'owner/repo.git'\"", "git", "github.com", CommandClass.Read, "owner/repo.git")]
    [InlineData("ssh git@github.com git-receive-pack 'owner/repo.git'", "git", "github.com", CommandClass.Write, "owner/repo.git")]
    [InlineData("C:/Windows/System32/OpenSSH/ssh.exe -p 2222 -i key -l deploy host.example", "deploy", "host.example", CommandClass.Write, null)]
    [InlineData("ssh -oUser=ops -p2222 box", "ops", "box", CommandClass.Write, null)]
    [InlineData("ssh ssh://git@example.com:2222/repo.git git-upload-archive repo", "git", "example.com", CommandClass.Read, "repo")]
    [InlineData("ssh -T git@github.com", "git", "github.com", CommandClass.Write, null)]
    [InlineData("ssh -- git@h git-lfs-authenticate o/r download", "git", "h", CommandClass.Read, "o/r")]
    [InlineData("ssh -p 2222 me@127.0.0.1 \"C:/Program Files/Git/mingw64/bin/git-receive-pack '/tmp/a b/repo.git'\"", "me", "127.0.0.1", CommandClass.Write, "/tmp/a b/repo.git")]
    [InlineData("ssh me@h \"C:/Program Files/Git/mingw64/bin/git-upload-pack.exe '/r.git'\"", "me", "h", CommandClass.Read, "/r.git")]
    public void Ssh_command_lines_give_host_user_class_and_repo(string line, string user, string host, CommandClass expected, string? repo)
    {
        var command = SshClientCommand.Parse(Split(line));
        Assert.Equal("ssh", command.Program);
        Assert.Equal(user, command.User);
        Assert.Equal(host, command.Host);
        Assert.Equal(expected, command.Class);
        Assert.Equal(repo, command.Repo);
    }

    [Fact]
    public void Other_clients_are_unknown_and_commit_signing_is_write()
    {
        Assert.Equal(CommandClass.Write, SshClientCommand.Parse(["ssh-keygen.exe", "-Y", "sign", "-f", "k.pub", "-n", "git"]).Class);
        Assert.Equal(CommandClass.Unknown, SshClientCommand.Parse(["python.exe", "agent.py"]).Class);
        Assert.Equal(CommandClass.Unknown, SshClientCommand.Parse([]).Class);
        Assert.Equal(CommandClass.Unknown, SshClientCommand.Parse(["ssh", "-p"]).Class);
    }

    [Fact]
    public async Task Protocol_reads_frames_and_rejects_a_bad_length()
    {
        var stream = new MemoryStream();
        await SshAgentProtocol.WriteMessageAsync(stream, [SshAgentProtocol.RequestIdentities], default);
        stream.Position = 0;
        var message = await SshAgentProtocol.ReadMessageAsync(stream, default);
        Assert.Equal(new[] { SshAgentProtocol.RequestIdentities }, message);
        Assert.Null(await SshAgentProtocol.ReadMessageAsync(stream, default));

        var huge = new MemoryStream([0x7F, 0xFF, 0xFF, 0xFF, 1]);
        await Assert.ThrowsAsync<InvalidDataException>(() => SshAgentProtocol.ReadMessageAsync(huge, default));
        Assert.Throws<InvalidDataException>(() => SshAgentProtocol.ParseSign([SshAgentProtocol.SignRequest, 0, 0, 0, 9, 1]));
    }

    [Theory]
    [InlineData(@"\\.\pipe\openssh-ssh-agent")]
    [InlineData(@"\.\pipe\openssh-ssh-agent")]
    [InlineData("//./pipe/openssh-ssh-agent")]
    [InlineData("openssh-ssh-agent")]
    public void Pipe_paths_in_any_form_become_one_form(string pipe) =>
        Assert.Equal(SshGate.DefaultUpstream, SshGate.NormalizePipePath(pipe));

    [Fact]
    public void Probe_says_degraded_when_ssh_auth_sock_is_not_the_gate()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-ssh-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(HardenState.NotHardened, SshGate.Probe(root).State);
            SshGate.Save(new SshGateState(), root);
            var probe = SshGate.Probe(root, () => SshGate.DefaultUpstream);
            Assert.Equal(HardenState.Degraded, probe.State);
            Assert.Contains("SSH_AUTH_SOCK", probe.Reason, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static List<string> Split(string line) =>
        CmdWarden.Agent.Identity.PowerShellInspector.SplitCommandLine(line).ToList();
}
