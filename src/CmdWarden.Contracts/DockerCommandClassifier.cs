namespace CmdWarden.Contracts;

/// <summary>
/// docker argv → Command Class (issue #42).
/// Prefer token parse; strip common client globals. Optional vault
/// DOCKER_AUTH_CONFIG inject is handled by Authorize, not classification.
/// </summary>
public static class DockerCommandClassifier
{
    private static readonly HashSet<string> HelpTokens = new(StringComparer.Ordinal)
    {
        "-h", "--help", "help",
    };

    private static readonly HashSet<string> VersionTokens = new(StringComparer.Ordinal)
    {
        "-v", "--version", "version",
    };

    private static readonly HashSet<string> FlagsWithValue = new(StringComparer.Ordinal)
    {
        // Normalized argv is lowercased (-H becomes -h; cannot distinguish from help short flag).
        "--host", "-c", "--context", "--config",
        "-l", "--log-level", "--tlscacert", "--tlscert", "--tlskey",
        "-f", "--file", "--filter", "--format",
    };

    private static readonly HashSet<string> ReadCommands = new(StringComparer.Ordinal)
    {
        "ps", "logs", "images", "image", "inspect", "info", "version", "events",
        "stats", "top", "port", "diff", "history", "search", "system", "context",
        "network", "volume", "plugin", "node", "service", "stack", "secret", "config",
        "container", "trust", "manifest", "builder", "buildx", "completion",
        "pull", // also handled at top; include for nested
    };

    private static readonly HashSet<string> WriteCommands = new(StringComparer.Ordinal)
    {
        "login", "logout", "push", "run", "create", "start", "stop", "restart",
        "kill", "rm", "rmi", "build", "commit", "tag", "import", "export",
        "load", "save", "update", "rename", "pause", "unpause", "attach", "exec",
        "cp", "wait", "swarm", "compose",
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

        // No first-class secret-export subcommand on docker CLI; helper get is out of band.
        // Plaintext login password flag remains write (auth mutation), not secret-reveal.

        var structure = ExtractStructure(tokens);
        if (structure.Count == 0)
            return CommandClass.Unknown;

        var head = structure[0];

        // Auth mutation
        if (head is "login" or "logout")
            return CommandClass.Write;

        // Registry write / high impact
        if (head == "push")
            return CommandClass.Write;
        if (head == "image" && structure.Count >= 2 && structure[1] == "push")
            return CommandClass.Write;
        if (head == "buildx" && structure.Any(t => t is "build" or "push" or "imagetools"))
        {
            // buildx build --push is write; buildx imagetools inspect is read-ish but rare
            if (structure.Contains("inspect") || structure.Contains("ls"))
                return CommandClass.Read;
            if (tokens.Contains("--push") || structure.Contains("push") || structure.Contains("build"))
                return CommandClass.Write;
        }

        // Registry read
        if (head == "pull")
            return CommandClass.Read;
        if (head == "image" && structure.Count >= 2 && structure[1] == "pull")
            return CommandClass.Read;

        // compose plugin
        if (head == "compose")
            return ClassifyCompose(structure, tokens);

        // Local / daemon ops
        if (head is "ps" or "logs" or "images" or "inspect" or "info" or "events"
            or "stats" or "top" or "port" or "diff" or "history" or "search")
            return CommandClass.Read;

        if (head is "system")
        {
            if (structure.Count >= 2 && structure[1] is "df" or "info" or "events" or "version")
                return CommandClass.Read;
            if (structure.Count >= 2 && structure[1] is "prune")
                return CommandClass.Write;
            return CommandClass.Read;
        }

        if (head is "network" or "volume" or "context" or "plugin" or "secret" or "config")
        {
            if (structure.Count >= 2 && structure[1] is "ls" or "list" or "inspect" or "prune")
            {
                if (structure[1] == "prune")
                    return CommandClass.Write;
                return CommandClass.Read;
            }

            if (structure.Count >= 2 && structure[1] is "create" or "rm" or "remove" or "connect"
                or "disconnect" or "update" or "use" or "import" or "export")
                return CommandClass.Write;
            return CommandClass.Unknown;
        }

        if (head is "container")
        {
            if (structure.Count >= 2 && structure[1] is "ls" or "list" or "ps" or "logs" or "inspect"
                or "top" or "port" or "stats" or "diff")
                return CommandClass.Read;
            if (structure.Count >= 2)
                return CommandClass.Write;
            return CommandClass.Unknown;
        }

        if (WriteCommands.Contains(head))
            return CommandClass.Write;

        if (ReadCommands.Contains(head))
            return CommandClass.Read;

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

    private static CommandClass ClassifyCompose(List<string> structure, List<string> tokens)
    {
        // docker compose [sub]
        if (structure.Count < 2)
            return CommandClass.Unknown;

        var sub = structure[1];
        if (sub is "push")
            return CommandClass.Write;
        if (sub is "pull")
            return CommandClass.Read;
        if (sub is "up" or "down" or "run" or "start" or "stop" or "restart" or "kill"
            or "rm" or "create" or "build" or "exec" or "cp" or "watch")
            return CommandClass.Write;
        if (sub is "ps" or "logs" or "config" or "images" or "top" or "port" or "events"
            or "version" or "ls" or "list")
            return CommandClass.Read;

        // compose build --push
        if (tokens.Contains("--push"))
            return CommandClass.Write;

        return CommandClass.Unknown;
    }

    private static List<string> ExtractStructure(List<string> tokens)
    {
        var structure = new List<string>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t == "--")
                break;

            if (t.StartsWith('-') && t.Contains('=', StringComparison.Ordinal))
                continue;

            if (t.StartsWith('-'))
            {
                if (FlagsWithValue.Contains(t))
                {
                    if (i + 1 < tokens.Count && !tokens[i + 1].StartsWith('-'))
                        i++;
                }

                continue;
            }

            structure.Add(t);
        }

        return structure;
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
                && (t.EndsWith("docker.exe", StringComparison.OrdinalIgnoreCase)
                    || t.Equals("docker", StringComparison.OrdinalIgnoreCase)))
                continue;
            list.Add(t.ToLowerInvariant());
        }

        return list;
    }
}
