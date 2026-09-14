namespace CmdWarden.Contracts;

/// <summary>
/// Strong gh vault keys (#208). Active slot <c>CmdWarden/gh/github.com</c>; one entry per user
/// <c>CmdWarden/gh/alice@github.com</c>. Hidden from the Secrets tab like every helper prefix.
/// </summary>
public static class GhVaultNames
{
    public const string Tool = "gh";
    public const string DefaultHost = "github.com";
    /// <summary>Stock gh CredMan target prefix; entries read <c>gh:&lt;host&gt;:&lt;user&gt;</c>.</summary>
    public const string StockPrefix = "gh:";

    public static string Prefix => VaultNames.HelperTargetPrefix(Tool);

    /// <summary>Key under the prefix: <c>host</c> for the active slot, <c>user@host</c> per user.</summary>
    public static string Key(string user, string host) =>
        string.IsNullOrEmpty(user) ? Norm(host) : user.Trim() + "@" + Norm(host);

    public static string Target(string key) => VaultNames.HelperTargetName(Tool, key);

    /// <summary>Key under the prefix: (user, host); user is empty for the active slot.</summary>
    public static (string User, string Host) Parse(string key)
    {
        var at = key.LastIndexOf('@');
        return at < 0 ? ("", Norm(key)) : (key[..at], Norm(key[(at + 1)..]));
    }

    /// <summary>Stock target <c>gh:&lt;host&gt;:&lt;user&gt;</c> to (host, user); null when the shape is wrong.</summary>
    public static (string Host, string User)? ParseStock(string target, string prefix = StockPrefix)
    {
        if (!target.StartsWith(prefix, StringComparison.Ordinal))
            return null;
        var rest = target[prefix.Length..];
        var colon = rest.IndexOf(':');
        return colon < 0 ? null : (Norm(rest[..colon]), rest[(colon + 1)..]);
    }

    public static string StockTarget(string host, string user, string prefix = StockPrefix) =>
        prefix + Norm(host) + ":" + user;

    /// <summary>gh's rule: GH_TOKEN serves github.com, *.ghe.com, and github.localhost; every other host is GHES.</summary>
    public static bool IsEnterprise(string host)
    {
        var h = Norm(host);
        return !(h == DefaultHost || h == "github.localhost" || h.EndsWith(".ghe.com", StringComparison.Ordinal));
    }

    public static string TokenEnvName(string host) => IsEnterprise(host) ? "GH_ENTERPRISE_TOKEN" : "GH_TOKEN";

    public static string Norm(string host) => host.Trim().ToLowerInvariant();
}
