namespace CmdWarden.Contracts;

/// <summary>
/// gh argv → Command Class (spike table; issues #29 / #35).
/// Prefer robust token parse over substring match.
/// </summary>
public static class GhCommandClassifier
{
    private static readonly HashSet<string> ReadSubs = new(StringComparer.Ordinal)
    {
        "list", "view", "status", "diff", "checks", "show", "get", "download", "browse",
    };

    private static readonly HashSet<string> WriteSubs = new(StringComparer.Ordinal)
    {
        "create", "edit", "close", "reopen", "merge", "comment", "delete", "ready",
        "add", "remove", "set", "update", "upload", "transfer", "archive", "unarchive",
        "lock", "unlock", "pin", "unpin", "develop", "mark", "unmark", "sync", "checkout",
        "run", "rerun", "cancel", "watch", "enable", "disable", "deploy",
    };

    private static readonly HashSet<string> HelpTokens = new(StringComparer.Ordinal)
    {
        "-h", "--help", "help",
    };

    private static readonly HashSet<string> VersionTokens = new(StringComparer.Ordinal)
    {
        "-v", "--version", "version",
    };

    /// <summary>
    /// Classify argv as passed by the shim (typically without the executable name).
    /// </summary>
    public static CommandClass Classify(IReadOnlyList<string> argv)
    {
        if (argv is null || argv.Count == 0)
            return CommandClass.Unknown;

        var tokens = Normalize(argv);
        if (tokens.Count == 0)
            return CommandClass.Unknown;

        // Pure help / version (including "gh --help", "auth --help", "pr create --help").
        if (IsHelpOrVersionOnly(tokens) || EndsWithHelp(tokens))
            return CommandClass.Read;

        // secret-reveal first (flags can appear anywhere).
        if (IsSecretReveal(tokens))
            return CommandClass.SecretReveal;

        var head = tokens[0];

        if (head == "auth")
            return ClassifyAuth(tokens);

        if (head is "pr" or "issue" or "repo" or "release" or "run" or "gist" or "project"
            or "label" or "workflow" or "codespace" or "secret" or "variable" or "extension"
            or "alias" or "config" or "ssh-key" or "gpg-key" or "ruleset" or "search" or "org"
            or "cache" or "attestation")
        {
            return ClassifyResource(tokens);
        }

        // api: treat as write (mutations common); refine later with method heuristics.
        if (head == "api")
            return CommandClass.Write;

        if (head is "status" or "browse")
            return CommandClass.Read;

        if (head is "completion")
            return CommandClass.Read;

        return CommandClass.Unknown;
    }

    /// <summary>
    /// True when argv is only help/version meta (no work that needs a token).
    /// </summary>
    public static bool IsHelpOnly(IReadOnlyList<string> argv)
    {
        if (argv is null || argv.Count == 0)
            return false;
        var tokens = Normalize(argv);
        return tokens.Count > 0 && (IsHelpOrVersionOnly(tokens) || EndsWithHelp(tokens));
    }

    /// <summary>
    /// True for gh auth login / refresh / logout / switch. Real gh refuses these when
    /// GH_TOKEN is set, so the grant carries no token for them (#198).
    /// </summary>
    public static bool IsKeyringAuthMutation(IReadOnlyList<string> argv)
    {
        if (argv is null || argv.Count == 0)
            return false;
        var tokens = Normalize(argv);
        return tokens.Count >= 2
            && tokens[0] == "auth"
            && tokens[1] is "login" or "refresh" or "logout" or "switch";
    }

    /// <summary>The auth sub-command (login, refresh, logout, switch) or null (#208).</summary>
    public static string? KeyringAuthVerb(IReadOnlyList<string> argv) =>
        IsKeyringAuthMutation(argv) ? Normalize(argv)[1] : null;

    /// <summary>
    /// Host named by argv <c>--hostname h</c>, <c>-R</c>/<c>--repo host/owner/repo</c>, or env
    /// <c>GH_HOST</c>, lower-cased; null when none. Strong gh routes GH_ENTERPRISE_TOKEN by it (#208).
    /// </summary>
    public static string? NamedHost(IReadOnlyList<string> argv, string? ghHostEnv)
    {
        var tokens = argv.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToList();
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            string? value = null;
            if (t is "--hostname" or "-R" or "--repo")
                value = i + 1 < tokens.Count ? tokens[i + 1] : null;
            else if (t.StartsWith("--hostname=", StringComparison.Ordinal) || t.StartsWith("--repo=", StringComparison.Ordinal))
                value = t[(t.IndexOf('=') + 1)..];
            if (value is null)
                continue;
            if (t.StartsWith("--hostname", StringComparison.Ordinal))
                return GhVaultNames.Norm(value);
            if (RepoHost(value) is { } host)
                return host;
        }
        return string.IsNullOrWhiteSpace(ghHostEnv) ? null : GhVaultNames.Norm(ghHostEnv);
    }

    /// <summary>host/owner/repo or https://host/owner/repo names a host; owner/repo does not.</summary>
    private static string? RepoHost(string value)
    {
        var v = value;
        var scheme = v.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
            v = v[(scheme + 3)..];
        var parts = v.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 && parts[0].Contains('.') ? GhVaultNames.Norm(parts[0]) : null;
    }

    private static CommandClass ClassifyAuth(List<string> tokens)
    {
        if (tokens.Count < 2)
            return CommandClass.Unknown;

        return tokens[1] switch
        {
            "token" => CommandClass.SecretReveal,
            "git-credential" => CommandClass.SecretReveal,
            "status" => CommandClass.Read,
            "login" or "logout" or "refresh" or "switch" or "setup-git" => CommandClass.Write,
            _ => CommandClass.Unknown,
        };
    }

    private static CommandClass ClassifyResource(List<string> tokens)
    {
        if (tokens.Count < 2)
            return CommandClass.Unknown;

        var sub = tokens[1];
        if (ReadSubs.Contains(sub))
            return CommandClass.Read;
        if (WriteSubs.Contains(sub))
            return CommandClass.Write;

        // Nested: gh workflow run, gh release create, etc. already covered by WriteSubs for run/create.
        // gh run list / view
        if (tokens[0] == "run" && ReadSubs.Contains(sub))
            return CommandClass.Read;
        if (tokens[0] == "run" && WriteSubs.Contains(sub))
            return CommandClass.Write;

        return CommandClass.Unknown;
    }

    private static bool IsSecretReveal(List<string> tokens)
    {
        // Anywhere: --show-token
        if (tokens.Any(t => t is "--show-token"))
            return true;

        // gh auth token [flags]
        if (tokens.Count >= 2 && tokens[0] == "auth" && tokens[1] == "token")
            return true;

        // gh auth git-credential get
        if (tokens.Count >= 3 && tokens[0] == "auth" && tokens[1] == "git-credential" && tokens[2] == "get")
            return true;

        return false;
    }

    private static bool IsHelpOrVersionOnly(List<string> tokens) =>
        tokens.TrueForAll(t => HelpTokens.Contains(t) || VersionTokens.Contains(t));

    private static bool EndsWithHelp(List<string> tokens)
    {
        // e.g. pr create --help, auth login -h
        if (tokens.Count == 0)
            return false;
        return HelpTokens.Contains(tokens[^1]);
    }

    private static List<string> Normalize(IReadOnlyList<string> argv)
    {
        var list = new List<string>(argv.Count);
        foreach (var a in argv)
        {
            if (string.IsNullOrWhiteSpace(a))
                continue;
            var t = a.Trim();
            if (list.Count == 0
                && (t.EndsWith("gh.exe", StringComparison.OrdinalIgnoreCase)
                    || t.Equals("gh", StringComparison.OrdinalIgnoreCase)))
                continue;
            list.Add(t.ToLowerInvariant());
        }

        return list;
    }
}
