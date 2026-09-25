using System.Text;

namespace CmdWarden.Contracts;

/// <summary>
/// Shared rendezvous for Session Agent IPC (named pipe).
/// </summary>
public static class AgentEndpoints
{
    public const string PipeNamePrefix = "CmdWarden";

    /// <summary>
    /// Per-user pipe name. Must stay ASCII-ish for Win32 pipe names.
    /// Override with env CW_PIPE_NAME (tests / multi-instance).
    /// </summary>
    public static string PipeName
    {
        get
        {
            var overrideName = Environment.GetEnvironmentVariable("CW_PIPE_NAME");
            if (!string.IsNullOrWhiteSpace(overrideName))
                return overrideName;
            return $"{PipeNamePrefix}-{Sanitize(OwnerUserName)}";
        }
    }

    /// <summary>
    /// The person whose Session Agent this process uses. An agent account such as CodexSandboxOffline
    /// runs with the environment of that person, so USERNAME names the person, not the account (#36).
    /// </summary>
    public static string OwnerUserName =>
        Environment.GetEnvironmentVariable("USERNAME") is { Length: > 0 } name ? name : Environment.UserName;

    /// <summary>True when this process runs under another account than the person it works for (#36).</summary>
    public static bool RunsAsOtherAccount =>
        !string.Equals(OwnerUserName, Environment.UserName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Dummy HTTP URI required by GrpcChannel when using a custom ConnectCallback.
    /// </summary>
    public static Uri GrpcChannelAddress { get; } = new("http://localhost");

    public static string Sanitize(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return "user";

        var sb = new StringBuilder(userName.Length);
        foreach (var c in userName)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_')
                sb.Append(c);
            else
                sb.Append('_');
        }

        return sb.Length == 0 ? "user" : sb.ToString();
    }
}
