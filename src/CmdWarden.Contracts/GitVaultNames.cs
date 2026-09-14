namespace CmdWarden.Contracts;

/// <summary>
/// GCM-shaped vault keys for git helper entries (#205).
/// Host <c>CmdWarden/git/https://github.com</c>, account
/// <c>CmdWarden/git/https://alice@github.com</c>, refresh
/// <c>CmdWarden/git/https://oauth-refresh-token.github.com</c>.
/// </summary>
public static class GitVaultNames
{
    public const string Tool = "git";

    public static string Prefix => VaultNames.HelperTargetPrefix(Tool);

    public readonly record struct Context(string Protocol, string Host, string Path, string Account)
    {
        public string HostKey => Format(Protocol, Host, Path, account: "");

        public string AccountKey =>
            string.IsNullOrEmpty(Account) ? HostKey : Format(Protocol, Host, Path, Account);

        public string RefreshKey
        {
            get
            {
                var host = Host;
                var colon = host.LastIndexOf(':');
                if (colon > 0)
                    host = host[..colon];
                return Protocol + "://oauth-refresh-token." + host;
            }
        }
    }

    public static string Target(string key) => VaultNames.HelperTargetName(Tool, key);

    public static string Format(string protocol, string host, string path, string account)
    {
        var key = protocol + "://" + (string.IsNullOrEmpty(account) ? "" : account + "@") + host;
        if (!string.IsNullOrEmpty(path))
            key += "/" + path.TrimStart('/');
        return key;
    }

    public static string StoreKey(string serverUrl, string username) =>
        Parse(serverUrl, username).AccountKey;

    public static Context Parse(string serverUrl, string username = "")
    {
        var raw = (serverUrl ?? "").Trim();
        if (raw.Length == 0)
            throw new ArgumentException("server_url is required.", nameof(serverUrl));

        var schemeSep = raw.IndexOf("://", StringComparison.Ordinal);
        if (schemeSep <= 0)
            throw new ArgumentException("server_url must be protocol://host.", nameof(serverUrl));

        var protocol = raw[..schemeSep];
        var rest = raw[(schemeSep + 3)..];

        var account = "";
        var at = rest.IndexOf('@');
        var slash = rest.IndexOf('/');
        if (at >= 0 && (slash < 0 || at < slash))
        {
            account = rest[..at];
            rest = rest[(at + 1)..];
            slash = rest.IndexOf('/');
        }

        string host;
        string path;
        if (slash < 0)
        {
            host = rest;
            path = "";
        }
        else
        {
            host = rest[..slash];
            path = rest[(slash + 1)..].TrimEnd('/');
        }

        if (!string.IsNullOrWhiteSpace(username))
            account = username.Trim();

        return new Context(protocol, host, path, account);
    }

    /// <summary>
    /// Stored keys to try on get, in order. Empty when two accounts match and no username.
    /// Host compare is case-insensitive. <paramref name="existingKeys"/> are suffixes under
    /// <c>CmdWarden/git/</c>.
    /// </summary>
    public static IReadOnlyList<string> LookupKeys(
        string serverUrl,
        string username,
        IReadOnlyList<string> existingKeys)
    {
        var requested = Parse(serverUrl, username);
        var matches = new List<(string Key, Context Ctx)>();
        foreach (var key in existingKeys)
        {
            Context ctx;
            try
            {
                ctx = Parse(key);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (!SameSite(requested, ctx))
                continue;
            matches.Add((key, ctx));
        }

        var result = new List<string>();
        if (!string.IsNullOrEmpty(requested.Account))
        {
            AddFirst(result, matches, m =>
                string.Equals(m.Ctx.Account, requested.Account, StringComparison.Ordinal));
            AddFirst(result, matches, m => string.IsNullOrEmpty(m.Ctx.Account));
            return result;
        }

        AddFirst(result, matches, m => string.IsNullOrEmpty(m.Ctx.Account));
        if (result.Count > 0)
            return result;

        var accounts = matches
            .Where(m => !string.IsNullOrEmpty(m.Ctx.Account))
            .Select(m => m.Key)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return accounts.Count == 1 ? accounts : Array.Empty<string>();
    }

    private static bool SameSite(Context a, Context b) =>
        a.Protocol.Equals(b.Protocol, StringComparison.OrdinalIgnoreCase)
        && a.Host.Equals(b.Host, StringComparison.OrdinalIgnoreCase)
        && a.Path.Equals(b.Path, StringComparison.OrdinalIgnoreCase);

    private static void AddFirst(
        List<string> result,
        List<(string Key, Context Ctx)> matches,
        Func<(string Key, Context Ctx), bool> predicate)
    {
        foreach (var match in matches)
        {
            if (!predicate(match))
                continue;
            result.Add(match.Key);
            return;
        }
    }
}
