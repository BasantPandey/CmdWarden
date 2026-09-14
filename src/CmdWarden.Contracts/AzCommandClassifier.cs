namespace CmdWarden.Contracts;

/// <summary>
/// az argv → Command Class (gate-only; issue #41).
/// Prefer token parse over substring match; strip common global flags.
/// </summary>
public static class AzCommandClassifier
{
    private static readonly HashSet<string> HelpTokens = new(StringComparer.Ordinal)
    {
        "-h", "--help", "help",
    };

    private static readonly HashSet<string> VersionTokens = new(StringComparer.Ordinal)
    {
        "-v", "--version", "version",
    };

    /// <summary>Flags that consume a following value (skipped when building structure).</summary>
    private static readonly HashSet<string> FlagsWithValue = new(StringComparer.Ordinal)
    {
        "-o", "--output", "--query", "--subscription", "-g", "--resource-group",
        "-n", "--name", "-l", "--location", "--ids", "--scope", "--assignee",
        "--role", "--resource", "--namespace", "--parent", "--vault-name",
        "--account-name", "--server", "--database", "--resource-type",
        "--api-version", "--uri", "--url", "--body", "--headers", "--method",
        "--username", "-u", "--password", "-p", "--tenant", "-t", "--client-id",
        "--client-secret", "--certificate", "--thumbprint", "--federated-token",
        "--identity-resource-id", "--file", "-f", "--parameters",
        "--template-file", "--template-uri",
    };

    private static readonly HashSet<string> ReadVerbs = new(StringComparer.Ordinal)
    {
        "list", "show", "get", "status", "check", "wait", "browse", "download",
        "export", "print", "view", "describe", "exists", "count", "table",
        "query", "find", "search", "monitor", "metrics", "logs", "tail",
    };

    private static readonly HashSet<string> WriteVerbs = new(StringComparer.Ordinal)
    {
        "create", "delete", "update", "set", "add", "remove", "assign", "deploy",
        "start", "stop", "restart", "deallocate", "resize", "apply", "enable",
        "disable", "attach", "detach", "import", "restore", "backup", "revoke",
        "rotate", "regenerate", "reset", "invoke", "run", "execute", "publish",
        "upload", "copy", "move", "rename", "lock", "unlock", "purge", "recover",
        "approve", "reject", "cancel", "retry", "scale", "upgrade", "install",
        "uninstall", "register", "unregister", "grant", "deny", "clear",
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

        if (IsHelpOrVersionOnly(tokens) || EndsWithHelp(tokens))
            return CommandClass.Read;

        if (IsSecretReveal(tokens))
            return CommandClass.SecretReveal;

        var structure = ExtractStructure(tokens);
        if (structure.Count == 0)
            return CommandClass.Unknown;

        // Top-level auth mutation
        if (structure[0] is "login" or "logout")
            return CommandClass.Write;

        if (structure[0] == "account" && structure.Count >= 2 && structure[1] == "clear")
            return CommandClass.Write;

        // configure / extension mutations
        if (structure[0] is "configure")
            return CommandClass.Write;

        if (structure[0] == "extension" && structure.Count >= 2
            && structure[1] is "add" or "remove" or "update")
            return CommandClass.Write;

        // rest can mutate; treat as write (safer than unknown for Trusted terminals)
        if (structure[0] == "rest")
            return CommandClass.Write;

        // Verb-based: last structural token often list/show/create
        var verb = structure[^1];
        if (ReadVerbs.Contains(verb))
            return CommandClass.Read;
        if (WriteVerbs.Contains(verb))
            return CommandClass.Write;

        // Two-level without trailing verb: e.g. "group" alone → unknown
        // Intermediate groups with known second token handled by verb sets.
        // az upgrade → write-ish
        if (structure[0] == "upgrade")
            return CommandClass.Write;

        return CommandClass.Unknown;
    }

    /// <summary>
    /// True when argv is only help/version meta.
    /// </summary>
    public static bool IsHelpOnly(IReadOnlyList<string> argv)
    {
        if (argv is null || argv.Count == 0)
            return false;
        var tokens = Normalize(argv);
        return tokens.Count > 0 && (IsHelpOrVersionOnly(tokens) || EndsWithHelp(tokens));
    }

    private static bool IsSecretReveal(List<string> tokens)
    {
        // Anywhere: get-access-token (even with --query)
        if (tokens.Contains("get-access-token"))
            return true;

        // account get-access-token (redundant but explicit)
        if (ContainsSequence(tokens, "account", "get-access-token"))
            return true;

        // SP create that can print password
        if (ContainsSequence(tokens, "ad", "sp", "create-for-rbac"))
            return true;
        if (ContainsSequence(tokens, "ad", "sp", "credential", "reset"))
            return true;

        // Key Vault secret material
        if (ContainsSequence(tokens, "keyvault", "secret", "show"))
            return true;
        if (ContainsSequence(tokens, "keyvault", "secret", "download"))
            return true;
        if (ContainsSequence(tokens, "keyvault", "secret", "backup"))
            return true;

        // Storage account keys
        if (ContainsSequence(tokens, "storage", "account", "keys", "list"))
            return true;
        if (ContainsSequence(tokens, "storage", "account", "keys", "renew"))
            return true;

        return false;
    }

    private static List<string> ExtractStructure(List<string> tokens)
    {
        var structure = new List<string>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];

            if (t is "--" )
                break;

            // Skip flag=value forms
            if (t.StartsWith('-') && t.Contains('=', StringComparison.Ordinal))
                continue;

            if (t.StartsWith('-'))
            {
                // Flag with separate value?
                var flag = t;
                if (FlagsWithValue.Contains(flag) || flag is "-o" or "-g" or "-n" or "-l" or "-u" or "-p" or "-t" or "-f")
                {
                    // consume following value if present and not another flag
                    if (i + 1 < tokens.Count && !tokens[i + 1].StartsWith('-'))
                        i++;
                }

                continue;
            }

            structure.Add(t);
        }

        return structure;
    }

    private static bool ContainsSequence(List<string> tokens, params string[] seq)
    {
        if (seq.Length == 0 || tokens.Count < seq.Length)
            return false;

        for (var i = 0; i <= tokens.Count - seq.Length; i++)
        {
            var ok = true;
            for (var j = 0; j < seq.Length; j++)
            {
                if (tokens[i + j] != seq[j])
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
                return true;
        }

        return false;
    }

    private static bool IsHelpOrVersionOnly(List<string> tokens) =>
        tokens.TrueForAll(t => HelpTokens.Contains(t) || VersionTokens.Contains(t));

    private static bool EndsWithHelp(List<string> tokens) =>
        tokens.Count > 0 && HelpTokens.Contains(tokens[^1]);

    private static List<string> Normalize(IReadOnlyList<string> argv)
    {
        var list = new List<string>(argv.Count);
        foreach (var a in argv)
        {
            if (string.IsNullOrWhiteSpace(a))
                continue;
            var t = a.Trim();
            if (list.Count == 0
                && (t.EndsWith("az.exe", StringComparison.OrdinalIgnoreCase)
                    || t.EndsWith("az.cmd", StringComparison.OrdinalIgnoreCase)
                    || t.Equals("az", StringComparison.OrdinalIgnoreCase)))
                continue;
            list.Add(t.ToLowerInvariant());
        }

        return list;
    }
}
