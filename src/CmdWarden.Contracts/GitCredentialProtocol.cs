using System.Text;

namespace CmdWarden.Contracts;

/// <summary>
/// Git credential helper line protocol (#206). One <c>key=value</c> line at a time,
/// blank line or EOF ends the list. A line with its newline may not exceed 65535 bytes.
/// </summary>
public static class GitCredentialProtocol
{
    public const int MaxLineBytes = 65535;

    public static GitCredentialAttrs Parse(TextReader stdin)
    {
        var protocol = "";
        var host = "";
        var path = "";
        var username = "";
        var password = "";
        var expiry = "";
        var refresh = "";
        var ephemeral = false;
        var capabilities = new List<string>();

        while (true)
        {
            var line = stdin.ReadLine();
            if (line is null || line.Length == 0)
                break;
            if (Encoding.UTF8.GetByteCount(line) + 1 > MaxLineBytes)
                continue;

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = line[..eq];
            var value = line[(eq + 1)..];
            switch (key)
            {
                case "protocol":
                    protocol = value;
                    break;
                case "host":
                    host = value;
                    break;
                case "path":
                    path = value.TrimStart('/').TrimEnd('/');
                    break;
                case "username":
                    username = value;
                    break;
                case "password":
                    password = value;
                    break;
                case "password_expiry_utc":
                    expiry = value;
                    break;
                case "oauth_refresh_token":
                    refresh = value;
                    break;
                case "ephemeral":
                    ephemeral = value is "1" or "true";
                    break;
                case "capability[]":
                    if (value.Length > 0)
                        capabilities.Add(value);
                    else
                        capabilities.Clear();
                    break;
            }
        }

        return new GitCredentialAttrs(
            protocol, host, path, username, password, expiry, refresh, ephemeral, capabilities);
    }

    /// <summary>
    /// Write a successful <c>get</c>. Capability lines first, then username and password
    /// in the order git's <c>credential_write</c> uses.
    /// </summary>
    public static void WriteGet(
        TextWriter stdout,
        GitCredentialAttrs request,
        string username,
        string password,
        string passwordExpiryUtc = "",
        string oauthRefreshToken = "")
    {
        foreach (var cap in request.Capabilities)
            stdout.Write("capability[]=" + cap + "\n");
        stdout.Write("username=" + username + "\n");
        stdout.Write("password=" + password + "\n");
        if (oauthRefreshToken.Length > 0)
            stdout.Write("oauth_refresh_token=" + oauthRefreshToken + "\n");
        if (passwordExpiryUtc.Length > 0)
            stdout.Write("password_expiry_utc=" + passwordExpiryUtc + "\n");
    }

    public static void WriteDeny(TextWriter stdout) => stdout.Write("quit=true\n");

    public static string DenyStderr(string serverUrl, string reason) =>
        ProductInfo.Name + ": git credential denied for " + serverUrl + " (" + reason + ")";
}

public sealed record GitCredentialAttrs(
    string Protocol,
    string Host,
    string Path,
    string Username,
    string Password,
    string PasswordExpiryUtc,
    string OauthRefreshToken,
    bool Ephemeral,
    IReadOnlyList<string> Capabilities)
{
    public string ServerUrl =>
        string.IsNullOrEmpty(Protocol) || string.IsNullOrEmpty(Host)
            ? ""
            : GitVaultNames.Format(Protocol, Host, Path, account: "");
}
