using CmdWarden.Contracts;
using CmdWarden.Contracts.Ssh;

namespace CmdWarden.Cli.Harden;

public sealed record SshHardenResult(string Upstream, string PipePath, string? GitSshCommand, bool GitSshCommandSet);

/// <summary>
/// Turn the ssh gate on (#39): write ssh.json, point the user SSH_AUTH_SOCK at the gate pipe, and
/// let git use the Windows OpenSSH client, which reads SSH_AUTH_SOCK as a pipe. The Session Agent
/// starts the gate when it starts.
/// </summary>
public static class SshHarden
{
    public const string GitSshCommandKey = "core.sshCommand";

    /// <param name="gitGlobalConfig">Tests only: the global git config file to change.</param>
    public static SshHardenResult Run(string? upstream = null, string? productRoot = null, bool setUserEnv = true,
        string? gitGlobalConfig = null)
    {
        var existing = SshGate.Load(productRoot);
        upstream = SshGate.NormalizePipePath(upstream ?? existing?.Upstream ?? SshGate.DefaultUpstream);
        if (!SshGate.PipeExists(upstream))
            throw new InvalidOperationException(
                $"No ssh-agent pipe {upstream}. Start the OpenSSH agent as admin: Set-Service ssh-agent -StartupType Automatic; Start-Service ssh-agent. Then add your key with ssh-add.");

        // A second harden keeps the values from before the first one.
        var state = existing ?? new SshGateState
        {
            PreviousAuthSock = Environment.GetEnvironmentVariable("SSH_AUTH_SOCK", EnvironmentVariableTarget.User),
        };
        state.Upstream = upstream;

        string? sshCommand = null;
        if (GitExe(productRoot) is { } git)
        {
            var config = new GitGlobalConfig(git, gitGlobalConfig);
            sshCommand = config.GetAll(GitSshCommandKey).FirstOrDefault();
            // The ssh of Git for Windows reads SSH_AUTH_SOCK as a Unix socket, not a pipe.
            if (string.IsNullOrWhiteSpace(sshCommand) && File.Exists(SshGate.WindowsSsh))
            {
                config.ReplaceAll(GitSshCommandKey, SshGate.WindowsSsh);
                state.GitSshCommandSet = true;
                sshCommand = SshGate.WindowsSsh;
            }
        }

        SshGate.Save(state, productRoot);
        if (setUserEnv)
            Environment.SetEnvironmentVariable("SSH_AUTH_SOCK", SshGate.AuthSock, EnvironmentVariableTarget.User);
        return new SshHardenResult(upstream, SshGate.PipePath, sshCommand, state.GitSshCommandSet);
    }

    /// <summary>Put SSH_AUTH_SOCK and core.sshCommand back, and remove ssh.json.</summary>
    public static bool Unharden(string? productRoot = null, bool setUserEnv = true, string? gitGlobalConfig = null)
    {
        var state = SshGate.Load(productRoot);
        if (state is null)
            return false;
        if (state.GitSshCommandSet && GitExe(productRoot) is { } git)
        {
            var config = new GitGlobalConfig(git, gitGlobalConfig);
            // Only our value goes; a value the person set since stays.
            if (config.GetAll(GitSshCommandKey).FirstOrDefault() == SshGate.WindowsSsh)
                config.UnsetAll(GitSshCommandKey);
        }
        if (setUserEnv && Environment.GetEnvironmentVariable("SSH_AUTH_SOCK", EnvironmentVariableTarget.User) is { } sock
            && string.Equals(SshGate.NormalizePipePath(sock), SshGate.PipePath, StringComparison.OrdinalIgnoreCase))
            Environment.SetEnvironmentVariable("SSH_AUTH_SOCK", state.PreviousAuthSock, EnvironmentVariableTarget.User);
        File.Delete(SshGate.StatePath(productRoot));
        return true;
    }

    private static string? GitExe(string? productRoot) =>
        new ToolPinStore(productRoot).TryGet(GitHarden.ToolId)?.Path is { } pinned && File.Exists(pinned)
            ? pinned
            : GitDiscoverer.FindRealGit();
}
