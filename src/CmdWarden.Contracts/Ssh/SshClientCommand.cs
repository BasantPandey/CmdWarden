using System.Text.RegularExpressions;

namespace CmdWarden.Contracts.Ssh;

/// <summary>
/// What a process that asks the ssh gate to sign wants to do (#39): the host and user from the
/// ssh command line, the remote command, and the command class. git runs
/// <c>ssh git@github.com "git-receive-pack 'owner/repo.git'"</c> for a push and git-upload-pack
/// for a fetch.
/// </summary>
public sealed partial record SshClientCommand(string Program, string? User, string? Host, string? RemoteCommand, CommandClass Class)
{
    /// <summary>OpenSSH client options that take a value (ssh(1)).</summary>
    private const string OptionsWithValue = "BbcDEeFIiJLlmOoPpQRSWw";

    /// <summary>
    /// Read the command line of the client (program first). ssh gives the class from the remote
    /// command; ssh-keygen -Y sign (git commit signing) is write; any other program is unknown.
    /// </summary>
    public static SshClientCommand Parse(IReadOnlyList<string> args)
    {
        var program = args.Count == 0 ? "" : Path.GetFileNameWithoutExtension(args[0]).ToLowerInvariant();
        if (program == "ssh-keygen")
            return new(program, null, null, null, args.Contains("-Y") ? CommandClass.Write : CommandClass.Unknown);
        if (program != "ssh")
            return new(program, null, null, null, CommandClass.Unknown);

        string? user = null;
        for (var i = 1; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg == "--")
            {
                i++;
                return i < args.Count ? Destination(args, i, user) : new(program, user, null, null, CommandClass.Unknown);
            }
            if (!arg.StartsWith('-') || arg.Length < 2)
                return Destination(args, i, user);
            for (var j = 1; j < arg.Length; j++)
            {
                if (!OptionsWithValue.Contains(arg[j]))
                    continue;
                var value = j + 1 < arg.Length ? arg[(j + 1)..] : ++i < args.Count ? args[i] : "";
                if (arg[j] == 'l')
                    user = value;
                else if (arg[j] == 'o' && value.StartsWith("User=", StringComparison.OrdinalIgnoreCase))
                    user = value[5..];
                break;
            }
        }
        return new(program, user, null, null, CommandClass.Unknown);
    }

    private static SshClientCommand Destination(IReadOnlyList<string> args, int at, string? user)
    {
        var destination = args[at];
        if (destination.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
        {
            destination = destination[6..];
            var slash = destination.IndexOf('/');
            if (slash >= 0)
                destination = destination[..slash];
            var colon = destination.LastIndexOf(':');
            if (colon > destination.LastIndexOf('@'))
                destination = destination[..colon];
        }
        var atSign = destination.LastIndexOf('@');
        if (atSign >= 0)
        {
            user = destination[..atSign];
            destination = destination[(atSign + 1)..];
        }
        var remote = at + 1 < args.Count ? string.Join(' ', args.Skip(at + 1)) : null;
        return new("ssh", user, destination, remote, ClassOf(remote));
    }

    /// <summary>
    /// git-upload-pack and git-upload-archive only read. git-receive-pack writes. A shell or any
    /// other remote command can do anything, so it is write. The git program can come with a path,
    /// for example C:/Program Files/Git/mingw64/bin/git-receive-pack.
    /// </summary>
    public static CommandClass ClassOf(string? remoteCommand)
    {
        var match = GitRemote().Match(remoteCommand ?? "");
        return match.Groups["verb"].Value switch
        {
            "upload-pack" or "upload-archive" => CommandClass.Read,
            "lfs-authenticate" when remoteCommand!.Contains(" download", StringComparison.Ordinal) => CommandClass.Read,
            _ => CommandClass.Write,
        };
    }

    /// <summary>The repo a git remote command names, for example owner/repo.git, or null.</summary>
    public string? Repo =>
        GitRemote().Match(RemoteCommand ?? "") is { Success: true } m && m.Groups["repo"].Value is { Length: > 0 } repo ? repo : null;

    [GeneratedRegex("""git-(?<verb>upload-pack|receive-pack|upload-archive|lfs-authenticate)(?:\.exe)?['"]?\s+(?:'(?<repo>[^']*)'|"(?<repo>[^"]*)"|(?<repo>\S+))""")]
    private static partial Regex GitRemote();
}
