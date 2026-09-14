namespace CmdWarden.Contracts;

/// <summary>One host block of gh's <c>hosts.yml</c>: users, active user, and any plaintext tokens.</summary>
public sealed class GhHostEntry
{
    public string Host { get; init; } = "";
    public List<string> Users { get; } = new();
    public string? ActiveUser { get; set; }
    /// <summary>Insecure-storage tokens by user; the empty key is the host-level (active) token.</summary>
    public Dictionary<string, string> Tokens { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// gh <c>hosts.yml</c> (#208). The file is a fixed two-level map that gh writes with four-space
/// indents, so a line reader is enough; <see cref="Strip"/> keeps every other line byte for byte.
/// </summary>
public static class GhHostsFile
{
    public const string TokenKey = "oauth_token:";

    public static string DefaultDir()
    {
        var dir = Environment.GetEnvironmentVariable("GH_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(dir))
            return dir;
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
            return Path.Combine(xdg, "gh");
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return string.IsNullOrEmpty(appData)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "gh")
            : Path.Combine(appData, "GitHub CLI");
    }

    public static string DefaultPath() => Path.Combine(DefaultDir(), "hosts.yml");

    public static IReadOnlyList<GhHostEntry> Read(string path) =>
        File.Exists(path) ? Parse(File.ReadAllLines(path)) : Array.Empty<GhHostEntry>();

    public static IReadOnlyList<GhHostEntry> Parse(IEnumerable<string> lines)
    {
        var hosts = new List<GhHostEntry>();
        GhHostEntry? host = null;
        var inUsers = false;
        string? user = null;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Length == 0 || line.TrimStart().StartsWith('#'))
                continue;
            var indent = line.Length - line.TrimStart().Length;
            var text = line.Trim();
            if (indent == 0)
            {
                host = new GhHostEntry { Host = GhVaultNames.Norm(KeyOf(text)) };
                hosts.Add(host);
                inUsers = false;
                user = null;
                continue;
            }
            if (host is null)
                continue;
            if (indent == 4)
            {
                inUsers = text == "users:";
                user = null;
                if (text.StartsWith("user:", StringComparison.Ordinal))
                    host.ActiveUser = ValueOf(text);
                else if (text.StartsWith(TokenKey, StringComparison.Ordinal))
                    host.Tokens[""] = ValueOf(text);
                continue;
            }
            if (indent == 8 && inUsers)
            {
                user = KeyOf(text);
                host.Users.Add(user);
                continue;
            }
            if (indent >= 12 && user is not null && text.StartsWith(TokenKey, StringComparison.Ordinal))
                host.Tokens[user] = ValueOf(text);
        }
        return hosts;
    }

    /// <summary>Remove every <c>oauth_token</c> line; all other lines stay as written. Null when nothing changed.</summary>
    public static string[]? Strip(string[] lines)
    {
        var kept = lines.Where(l => !l.Trim().StartsWith(TokenKey, StringComparison.Ordinal)).ToArray();
        return kept.Length == lines.Length ? null : kept;
    }

    public static bool StripFile(string path)
    {
        if (!File.Exists(path) || Strip(File.ReadAllLines(path)) is not { } kept)
            return false;
        File.WriteAllLines(path, kept);
        return true;
    }

    private static string KeyOf(string text)
    {
        var colon = text.IndexOf(':');
        return (colon < 0 ? text : text[..colon]).Trim().Trim('"', '\'');
    }

    private static string ValueOf(string text)
    {
        var colon = text.IndexOf(':');
        return colon < 0 ? "" : text[(colon + 1)..].Trim().Trim('"', '\'');
    }
}
