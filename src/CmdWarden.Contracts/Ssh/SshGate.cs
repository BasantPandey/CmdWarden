using System.Text.Json;

namespace CmdWarden.Contracts.Ssh;

/// <summary>
/// The ssh gate (#39): the Session Agent serves an ssh-agent pipe, asks the Approval Gate before
/// each sign, and forwards the request to the real agent. ssh.json in the product root turns it on
/// and holds what cw harden ssh changed, so cw unharden ssh can put it back.
/// </summary>
public sealed class SshGateState
{
    /// <summary>The real agent pipe. The Windows OpenSSH agent (and 1Password) use this name.</summary>
    public string Upstream { get; set; } = SshGate.DefaultUpstream;
    /// <summary>The user SSH_AUTH_SOCK before harden, or null when it was not set.</summary>
    public string? PreviousAuthSock { get; set; }
    /// <summary>True when harden set git core.sshCommand (it was not set before).</summary>
    public bool GitSshCommandSet { get; set; }
}

public static class SshGate
{
    public const string Tool = "ssh";
    public const string DefaultUpstream = @"\\.\pipe\openssh-ssh-agent";
    public const string PipePrefix = @"\\.\pipe\";
    public const string WindowsSsh = "C:/Windows/System32/OpenSSH/ssh.exe";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>The pipe name of the gate, per user like the Session Agent pipe.</summary>
    public static string PipeName => AgentEndpoints.PipeName + "-ssh";

    public static string PipePath => PipePrefix + PipeName;

    /// <summary>
    /// The value for SSH_AUTH_SOCK. Windows OpenSSH reads //./pipe/name like the backslash form.
    /// Git Bash passes the slash form on as it is, but it cuts the two leading backslashes of the
    /// backslash form to one, and then the pipe is not found.
    /// </summary>
    public static string AuthSock => "//./pipe/" + PipeName;

    public static string StatePath(string? productRoot = null) => Path.Combine(productRoot ?? ProductPaths.Root(), "ssh.json");

    public static SshGateState? Load(string? productRoot = null)
    {
        var path = StatePath(productRoot);
        return File.Exists(path) ? JsonSerializer.Deserialize<SshGateState>(File.ReadAllText(path), JsonOptions) : null;
    }

    public static void Save(SshGateState state, string? productRoot = null)
    {
        var path = StatePath(productRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOptions));
    }

    /// <summary>The \\.\pipe\name form of a pipe name or of a pipe path in any usual form.</summary>
    public static string NormalizePipePath(string pipe)
    {
        var text = pipe.Trim().Replace('/', '\\');
        var at = text.IndexOf("pipe\\", StringComparison.OrdinalIgnoreCase);
        return PipePrefix + (at >= 0 ? text[(at + 5)..] : text.TrimStart('\\'));
    }

    public static bool PipeExists(string pipePath)
    {
        var name = pipePath.StartsWith(PipePrefix, StringComparison.OrdinalIgnoreCase) ? pipePath[PipePrefix.Length..] : pipePath;
        try
        {
            return Directory.EnumerateFiles(PipePrefix).Any(p => string.Equals(Path.GetFileName(p), name, StringComparison.OrdinalIgnoreCase));
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Harden status for cw harden --list and the Vault app.</summary>
    public static HardenedToolStatus Probe(string? productRoot = null, Func<string?>? userAuthSock = null)
    {
        var state = Load(productRoot);
        if (state is null)
            return new(Tool, HardenState.NotHardened, null, null);
        var sock = (userAuthSock ?? (() => Environment.GetEnvironmentVariable("SSH_AUTH_SOCK", EnvironmentVariableTarget.User)))();
        if (sock is null || !string.Equals(NormalizePipePath(sock), PipePath, StringComparison.OrdinalIgnoreCase))
            return new(Tool, HardenState.Degraded, state.Upstream, $"SSH_AUTH_SOCK is not {PipePath}; run cw harden ssh");
        if (!PipeExists(state.Upstream))
            return new(Tool, HardenState.Degraded, state.Upstream, $"the real ssh-agent pipe {state.Upstream} is not there; start the ssh-agent service");
        return new(Tool, HardenState.Hardened, state.Upstream, null, "sign requests go through the Approval Gate");
    }
}
