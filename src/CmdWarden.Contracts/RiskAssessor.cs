namespace CmdWarden.Contracts;

/// <summary>How much harm one command can do (#35). Policy asks for High and can auto-allow Low.</summary>
public enum RiskLevel
{
    Low,
    Normal,
    High,
}

/// <summary>The risk of one command and the one line that says what it does, or null when nothing stands out.</summary>
public sealed record RiskAssessment(RiskLevel Level, string? Impact)
{
    public static readonly RiskAssessment Plain = new(RiskLevel.Normal, null);
}

/// <summary>
/// Risk signals for one command (#35): a push to the default branch, a force push, a delete, and a
/// secret-reveal. The repo and branch come from the .git folder of the working folder.
/// ponytail: git push, gh and az deletes, and gh pr merge only. Other commands are Normal.
/// </summary>
public static class RiskAssessor
{
    public const string CannotUndo = "You cannot undo this.";

    public static RiskAssessment Assess(string tool, IReadOnlyList<string> argv, CommandClass commandClass,
        string? workingDirectory, string? secretName = null)
    {
        var risk = tool switch
        {
            "git" => Git(argv, workingDirectory),
            "gh" => Gh(argv, workingDirectory),
            "az" => Az(argv),
            _ => RiskAssessment.Plain,
        };
        if (risk.Impact is null && commandClass == CommandClass.SecretReveal)
            return risk with { Impact = $"Shows the {(string.IsNullOrEmpty(secretName) ? "secret" : secretName)} value to the app that runs this." };
        return risk;
    }

    /// <summary>The words that name a command for "first use": git push, gh pr merge, az group delete.</summary>
    public static string Verb(string tool, IReadOnlyList<string> argv)
    {
        if (tool == "git")
        {
            var (_, command) = GitGlobals(argv);
            return command >= 0 ? argv[command] : "";
        }
        return string.Join(' ', Positionals(argv, "-R", "--repo").Take(tool == "docker" ? 1 : 2));
    }

    private static RiskAssessment Git(IReadOnlyList<string> argv, string? workingDirectory)
    {
        var (dir, command) = GitGlobals(argv);
        if (command < 0 || argv[command] != "push")
            return RiskAssessment.Plain;
        var folder = dir is null ? workingDirectory : Path.Combine(workingDirectory ?? "", dir);
        var repo = GitRepoInfo.TryRead(folder);

        bool force = false, delete = false, mirror = false, many = false;
        var positionals = new List<string>();
        var args = argv.Skip(command + 1).ToList();
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a == "--")
            {
                positionals.AddRange(args.Skip(i + 1));
                break;
            }
            if (a is "-o" or "--push-option" or "--receive-pack" or "--exec" or "--repo")
            {
                i++;
                continue;
            }
            if (a is "-f" or "--force" || a.StartsWith("--force-with-lease", StringComparison.Ordinal))
                force = true;
            else if (a is "-d" or "--delete")
                delete = true;
            else if (a == "--mirror")
                mirror = true;
            else if (a is "--all" or "--branches" or "--tags")
                many = true;
            else if (!a.StartsWith('-'))
                positionals.Add(a);
            else if (a.Length > 2 && a[1] != '-')
            {
                // Short flags together, for example -uf.
                force |= a.Contains('f');
                delete |= a.Contains('d');
            }
        }

        var remote = positionals.Count > 0 ? positionals[0] : repo?.RemoteFor(repo.CurrentBranch) ?? "origin";
        var repoName = repo?.RepoName(remote) ?? remote;
        var targets = new List<string>();
        foreach (var spec in positionals.Skip(1))
        {
            var s = spec;
            if (s.StartsWith('+'))
            {
                force = true;
                s = s[1..];
            }
            if (s.StartsWith(':'))
            {
                delete = true;
                s = s[1..];
            }
            var colon = s.IndexOf(':');
            var dst = colon >= 0 ? s[(colon + 1)..] : s;
            if (dst == "HEAD")
                dst = repo?.CurrentBranch ?? "";
            if (dst.StartsWith("refs/heads/", StringComparison.Ordinal))
                dst = dst["refs/heads/".Length..];
            if (dst.Length > 0)
                targets.Add(dst);
        }
        if (positionals.Count <= 1 && !many && !mirror && repo?.CurrentBranch is { } current)
            targets.Add(current);

        var defaultBranch = repo is not null && repo.DefaultBranches.TryGetValue(remote, out var d) ? d : null;
        var toDefault = defaultBranch is not null && targets.Contains(defaultBranch);
        var where = targets.Count == 1 ? $"{targets[0]} of {repoName}" : repoName;

        if (mirror)
            return new(RiskLevel.High, $"Mirrors every ref to {repoName}, and deletes remote branches that are not local. {CannotUndo}");
        if (delete && toDefault)
            return new(RiskLevel.High, $"Deletes {defaultBranch} of {repoName}. {CannotUndo}");
        if (force && toDefault)
            return new(RiskLevel.High, $"Force-pushes to {defaultBranch} of {repoName}. {CannotUndo}");
        if (delete)
            return new(RiskLevel.Normal, $"Deletes branch {where}.");
        if (force)
            return new(RiskLevel.Normal, $"Force-pushes to {where}. Commits on the remote can be lost.");
        if (toDefault)
            return new(RiskLevel.Normal, $"Pushes to {defaultBranch} of {repoName}.");
        if (many)
            return new(RiskLevel.Normal, $"Pushes many refs to {repoName}.");
        if (targets.Count > 0 && defaultBranch is not null)
            return new(RiskLevel.Low, $"Pushes to {where}. Not the default branch ({defaultBranch}).");
        return new(RiskLevel.Normal, $"Pushes to {where}.");
    }

    private static RiskAssessment Gh(IReadOnlyList<string> argv, string? workingDirectory)
    {
        var words = Positionals(argv, "-R", "--repo");
        if (words.Count < 2)
            return RiskAssessment.Plain;
        var repo = FlagValue(argv, "-R", "--repo")
            ?? (GitRepoInfo.TryRead(workingDirectory) is { } info ? info.RepoName(info.RemoteFor(info.CurrentBranch)) : null);
        var inRepo = repo is null ? "" : $" in {repo}";
        return (words[0], words[1]) switch
        {
            ("repo", "delete") => new(RiskLevel.High, $"Deletes the repository {(words.Count > 2 ? words[2] : repo ?? "of this folder")}. {CannotUndo}"),
            (var noun, "delete") => new(RiskLevel.High, $"Deletes {noun}{(words.Count > 2 ? " " + words[2] : "")}{inRepo}. {CannotUndo}"),
            ("pr", "merge") => new(RiskLevel.Normal, $"Merges pull request{(words.Count > 2 ? " " + words[2] : "")}{inRepo}."),
            _ => RiskAssessment.Plain,
        };
    }

    private static RiskAssessment Az(IReadOnlyList<string> argv)
    {
        var words = Positionals(argv);
        return words.Contains("delete") || words.Contains("purge")
            ? new(RiskLevel.High, $"Deletes Azure resources: az {string.Join(' ', argv)}. {CannotUndo}")
            : RiskAssessment.Plain;
    }

    /// <summary>The -C folder and the index of the git subcommand, or -1.</summary>
    private static (string? Dir, int Command) GitGlobals(IReadOnlyList<string> argv)
    {
        string? dir = null;
        for (var i = 0; i < argv.Count; i++)
        {
            var t = argv[i];
            if (t == "-C" && i + 1 < argv.Count)
            {
                dir = dir is null ? argv[++i] : Path.Combine(dir, argv[++i]);
                continue;
            }
            if (t is "-c" or "--git-dir" or "--work-tree" or "--namespace" or "--super-prefix" or "--config-env" or "--exec-path")
            {
                i++;
                continue;
            }
            if (!t.StartsWith('-'))
                return (dir, i);
        }
        return (dir, -1);
    }

    /// <summary>Words that are not flags. The value after each named flag is skipped too.</summary>
    private static List<string> Positionals(IReadOnlyList<string> argv, params string[] flagsWithValue)
    {
        var words = new List<string>();
        for (var i = 0; i < argv.Count; i++)
        {
            if (flagsWithValue.Contains(argv[i]))
                i++;
            else if (!argv[i].StartsWith('-'))
                words.Add(argv[i]);
        }
        return words;
    }

    private static string? FlagValue(IReadOnlyList<string> argv, params string[] names)
    {
        for (var i = 0; i < argv.Count; i++)
        {
            if (names.Contains(argv[i]) && i + 1 < argv.Count)
                return argv[i + 1];
            foreach (var n in names.Where(n => n.StartsWith("--", StringComparison.Ordinal)))
                if (argv[i].StartsWith(n + "=", StringComparison.Ordinal))
                    return argv[i][(n.Length + 1)..];
        }
        return null;
    }
}
