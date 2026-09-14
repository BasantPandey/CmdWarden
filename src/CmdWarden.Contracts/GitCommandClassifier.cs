namespace CmdWarden.Contracts;

/// <summary>
/// git argv → Command Class (gate-only; issue #40).
/// Prefer robust token parse over substring match; strip global options before the subcommand.
/// </summary>
public static class GitCommandClassifier
{
    private static readonly HashSet<string> ReadCommands = new(StringComparer.Ordinal)
    {
        "status", "log", "diff", "show", "branch", "rev-parse", "describe", "ls-files",
        "ls-remote", "fetch", "pull", "clone", "blame", "shortlog", "reflog", "whatchanged",
        "cat-file", "rev-list", "symbolic-ref", "name-rev", "for-each-ref", "ls-tree",
        "version", "help", "var", "check-ignore", "check-attr", "check-mailmap",
        "count-objects", "diff-files", "diff-index", "diff-tree", "merge-base",
    };

    private static readonly HashSet<string> WriteCommands = new(StringComparer.Ordinal)
    {
        "push", "commit", "add", "checkout", "switch", "merge", "rebase", "reset", "stash",
        "clean", "rm", "mv", "restore", "cherry-pick", "revert", "bisect", "am", "apply",
        "init", "worktree", "notes", "replace", "gc", "repack", "prune", "bundle",
        "format-patch", "send-email", "request-pull", "archive", "submodule", "sparse-checkout",
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

        // Pure help / version (including "git --help", "push --help").
        if (IsHelpOrVersionOnly(tokens) || EndsWithHelp(tokens))
            return CommandClass.Read;

        // Elevated / secret-adjacent globals before anything else.
        if (HasSecretAdjacentConfig(tokens))
            return CommandClass.SecretReveal;

        if (IsSecretReveal(tokens))
            return CommandClass.SecretReveal;

        var command = ExtractPrimaryCommand(tokens);
        if (command is null)
            return CommandClass.Unknown;

        // Typed helper dumps: git credential-manager get, credential-store, ... (#197).
        if (command.StartsWith("credential-", StringComparison.Ordinal))
            return CommandClass.SecretReveal;

        if (command == "credential")
            return ClassifyCredential(tokens);

        if (command == "config")
            return ClassifyConfig(tokens);

        if (command == "remote")
            return ClassifyRemote(tokens);

        if (command == "tag")
            return ClassifyTag(tokens);

        if (ReadCommands.Contains(command))
            return CommandClass.Read;

        if (WriteCommands.Contains(command))
            return CommandClass.Write;

        return CommandClass.Unknown;
    }

    /// <summary>
    /// True when argv is only help/version meta (no work that needs network auth).
    /// </summary>
    public static bool IsHelpOnly(IReadOnlyList<string> argv)
    {
        if (argv is null || argv.Count == 0)
            return false;
        var tokens = Normalize(argv);
        return tokens.Count > 0 && (IsHelpOrVersionOnly(tokens) || EndsWithHelp(tokens));
    }

    private static CommandClass ClassifyCredential(List<string> tokens)
    {
        // After "credential", next non-global token is the action.
        var action = TokenAfterCommand(tokens, "credential");
        return action switch
        {
            "fill" or "get" => CommandClass.SecretReveal,
            "approve" or "reject" or "store" or "erase" => CommandClass.Write,
            _ => CommandClass.Unknown,
        };
    }

    private static CommandClass ClassifyConfig(List<string> tokens)
    {
        // Any credential.* / helper mutation is write (harden regression).
        if (tokens.Any(t => t.Contains("credential", StringComparison.Ordinal)
                            || t.Contains("http.extraheader", StringComparison.Ordinal)))
            return CommandClass.Write;

        // Read-only config forms.
        if (tokens.Any(t => t is "--get" or "--get-all" or "--get-regexp" or "--list" or "-l"
                or "--name-only" or "--show-origin" or "--show-scope"))
            return CommandClass.Read;

        // config --get KEY without flag form is rare; bare "config KEY" is get (read).
        var after = TokenAfterCommand(tokens, "config");
        if (after is null)
            return CommandClass.Unknown;

        // Setting: config [--global] key value  OR  --add / --unset / --replace-all
        if (tokens.Any(t => t is "--add" or "--unset" or "--unset-all" or "--replace-all"
                or "--remove-section" or "--rename-section" or "--global" or "--system" or "--local"
                or "--worktree" or "--file" or "-f"))
        {
            // --global alone with --get still read (handled above). With a key+value → write.
            if (tokens.Any(t => t is "--get" or "--get-all" or "--get-regexp" or "--list" or "-l"))
                return CommandClass.Read;
            return CommandClass.Write;
        }

        // config key value  (two+ non-flag args after config) → write; single key → read
        var args = NonFlagArgsAfter(tokens, "config");
        if (args.Count >= 2)
            return CommandClass.Write;
        if (args.Count == 1)
            return CommandClass.Read;

        return CommandClass.Unknown;
    }

    private static CommandClass ClassifyRemote(List<string> tokens)
    {
        var action = TokenAfterCommand(tokens, "remote");
        if (action is null or "-v" or "--verbose")
            return CommandClass.Read;

        if (action is "show" or "get-url")
            return CommandClass.Read;

        // set-url with embedded userinfo is auth mutation (still write class).
        if (action is "add" or "remove" or "rm" or "rename" or "set-url" or "set-branches"
            or "set-head" or "prune" or "update")
            return CommandClass.Write;

        return CommandClass.Unknown;
    }

    private static CommandClass ClassifyTag(List<string> tokens)
    {
        // Listing tags is read; creating annotated/lightweight tags is write.
        if (tokens.Any(t => t is "-a" or "--annotate" or "-s" or "--sign" or "-u" or "--local-user"
                or "-d" or "--delete" or "-f" or "--force" or "-m" or "--message" or "-F" or "--file"))
            return CommandClass.Write;

        var args = NonFlagArgsAfter(tokens, "tag");
        // git tag  → list (read); git tag v1.0 → create (write)
        return args.Count == 0 ? CommandClass.Read : CommandClass.Write;
    }

    private static bool IsSecretReveal(List<string> tokens)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] != "credential")
                continue;
            if (i + 1 < tokens.Count && tokens[i + 1] is "fill" or "get")
                return true;
        }

        return false;
    }

    /// <summary>
    /// git -c credential.helper=... / -c http.extraHeader=... / -c core.askpass=... is secret-adjacent (#197).
    /// </summary>
    private static bool HasSecretAdjacentConfig(List<string> tokens)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t == "-c" && i + 1 < tokens.Count)
            {
                var spec = tokens[i + 1];
                if (IsSecretAdjacentSpec(spec))
                    return true;
            }

            if (t.StartsWith("-c", StringComparison.Ordinal) && t.Length > 2)
            {
                if (IsSecretAdjacentSpec(t[2..]))
                    return true;
            }

            if (t.StartsWith("--config-env=", StringComparison.Ordinal))
            {
                if (IsSecretAdjacentSpec(t["--config-env=".Length..]))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Strong git (#207): GIT_CONFIG_KEY_&lt;n&gt; in the caller env carries the same weight as -c.
    /// The child env names these keys; the shim must not see them as ordinary config.
    /// </summary>
    public const string ConfigEnvPrefix = "GIT_CONFIG_KEY_";

    public static readonly IReadOnlyList<string> StrongStripEnv =
        ["GIT_CONFIG_GLOBAL", "GIT_CONFIG_SYSTEM", "GIT_CONFIG_NOSYSTEM"];

    public static bool HasSecretAdjacentConfigEnv(IEnumerable<KeyValuePair<string, string>> callerEnv) =>
        callerEnv.Any(kv => kv.Key.StartsWith(ConfigEnvPrefix, StringComparison.OrdinalIgnoreCase)
                            && IsSecretAdjacentSpec(kv.Value.Trim().ToLowerInvariant()));

    private static bool IsSecretAdjacentSpec(string spec) =>
        spec.StartsWith("credential.", StringComparison.Ordinal)
        || spec.StartsWith("http.extraheader", StringComparison.Ordinal)
        || spec.StartsWith("core.askpass", StringComparison.Ordinal);

    private static string? ExtractPrimaryCommand(List<string> tokens)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];

            // Long options with =value
            if (t.StartsWith("--git-dir=", StringComparison.Ordinal)
                || t.StartsWith("--work-tree=", StringComparison.Ordinal)
                || t.StartsWith("--namespace=", StringComparison.Ordinal)
                || t.StartsWith("--super-prefix=", StringComparison.Ordinal)
                || t.StartsWith("--config-env=", StringComparison.Ordinal)
                || t.StartsWith("--exec-path=", StringComparison.Ordinal)
                || t.StartsWith("--list-cmds=", StringComparison.Ordinal))
                continue;

            // Long options taking a separate argument
            if (t is "--git-dir" or "--work-tree" or "--namespace" or "--super-prefix"
                or "--config-env" or "--exec-path")
            {
                i++; // skip value
                continue;
            }

            // Boolean-ish globals
            if (t is "--bare" or "--no-replace-objects" or "--literal-pathspecs"
                or "--glob-pathspecs" or "--noglob-pathspecs" or "--icase-pathspecs"
                or "--no-optional-locks" or "--paginate" or "--no-pager" or "-p" or "-P"
                or "--html-path" or "--man-path" or "--info-path" or "--attr-source")
                continue;

            // -C path, -c name=value
            if (t is "-C" or "-c")
            {
                i++; // skip value
                continue;
            }

            if (t.StartsWith("-c", StringComparison.Ordinal) && t.Length > 2)
                continue;

            // Skip other dash options that are not the command
            if (t.StartsWith('-'))
                continue;

            return t;
        }

        return null;
    }

    private static string? TokenAfterCommand(List<string> tokens, string command)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] != command)
                continue;
            for (var j = i + 1; j < tokens.Count; j++)
            {
                if (tokens[j].StartsWith('-'))
                    continue;
                return tokens[j];
            }

            return null;
        }

        return null;
    }

    private static List<string> NonFlagArgsAfter(List<string> tokens, string command)
    {
        var result = new List<string>();
        var seen = false;
        foreach (var t in tokens)
        {
            if (!seen)
            {
                if (t == command)
                    seen = true;
                continue;
            }

            if (t.StartsWith('-'))
                continue;
            result.Add(t);
        }

        return result;
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
                && (t.EndsWith("git.exe", StringComparison.OrdinalIgnoreCase)
                    || t.Equals("git", StringComparison.OrdinalIgnoreCase)))
                continue;
            // Keep original case for values after -c; classify uses lower for commands.
            // Lowercase everything for stable matching (URLs/userinfo still lowercased - ok for class).
            list.Add(t.ToLowerInvariant());
        }

        return list;
    }
}
