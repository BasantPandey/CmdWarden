namespace CmdWarden.Contracts;

/// <summary>
/// A login stores its hosts in the credential comment. The blob stays the password.
/// </summary>
public static class LoginSites
{
    public const string Prefix = "sites:";
    public const int MaxCommentLength = 256;
    public const int MaxAccountLength = 513;

    public static bool IsLogin(string? account, string? sites) =>
        !string.IsNullOrWhiteSpace(account) || !string.IsNullOrWhiteSpace(sites);

    public static string Account(string? account)
    {
        var raw = account ?? "";
        foreach (var c in raw)
        {
            if (char.IsControl(c))
                throw new ArgumentException("Site account must not contain control characters.");
        }
        var text = raw.Trim();
        if (text.Length == 0)
            throw new ArgumentException("Site account is required.");
        if (text.Length > MaxAccountLength)
            throw new ArgumentException($"Site account exceeds {MaxAccountLength} characters.");
        return text;
    }

    public static string Format(string? sites)
    {
        var hosts = Hosts(sites);
        var comment = Prefix + string.Join(',', hosts);
        if (comment.Length > MaxCommentLength)
            throw new ArgumentException($"Site list exceeds {MaxCommentLength} characters.");
        return comment;
    }

    public static bool TryParse(string? comment, out string sites)
    {
        sites = "";
        if (string.IsNullOrEmpty(comment) || !comment.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        var rest = comment[Prefix.Length..];
        if (rest.Length == 0)
            return false;
        try
        {
            Hosts(rest);
        }
        catch (ArgumentException)
        {
            return false;
        }
        sites = rest;
        return true;
    }

    private static List<string> Hosts(string? sites)
    {
        if (string.IsNullOrWhiteSpace(sites))
            throw new ArgumentException("Site list is required.");
        var hosts = new List<string>();
        foreach (var part in sites.Split(','))
        {
            var host = part.Trim();
            if (host.Length == 0)
                throw new ArgumentException("Site list has an empty host.");
            foreach (var c in host)
            {
                if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or ':' or '*'))
                    throw new ArgumentException("Site host has a character that is not allowed.");
            }
            hosts.Add(host);
        }
        return hosts;
    }
}
