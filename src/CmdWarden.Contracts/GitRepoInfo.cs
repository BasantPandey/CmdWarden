namespace CmdWarden.Contracts;

/// <summary>
/// What a git working folder says about itself (#35): the current branch, and per remote its URL and
/// default branch. Read from the files under .git, so no git process runs. Null parts are unknown.
/// </summary>
public sealed record GitRepoInfo(
    string? CurrentBranch,
    IReadOnlyDictionary<string, string> RemoteUrls,
    IReadOnlyDictionary<string, string> DefaultBranches,
    IReadOnlyDictionary<string, string> BranchRemotes)
{
    /// <summary>The repo of a working folder, or null when the folder is in no git repo.</summary>
    public static GitRepoInfo? TryRead(string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
            return null;
        try
        {
            return FindGitDir(workingDirectory) is { } gitDir ? Read(gitDir) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The remote a push of <paramref name="branch"/> goes to: its upstream remote, else origin.</summary>
    public string RemoteFor(string? branch) =>
        branch is not null && BranchRemotes.TryGetValue(branch, out var remote) ? remote : "origin";

    /// <summary>"owner/repo" for a GitHub-style URL of the remote, else the URL, else the remote name.</summary>
    public string RepoName(string remote)
    {
        if (!RemoteUrls.TryGetValue(remote, out var url))
            return remote;
        var path = url.Trim();
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            path = path[..^4];
        // A remote in a local folder: its folder name is enough.
        if (Path.IsPathFullyQualified(path) || path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            return Path.GetFileName(path.TrimEnd('/', '\\'));
        // https://host/owner/repo, ssh://git@host/owner/repo, git@host:owner/repo
        var start = path.IndexOf("://", StringComparison.Ordinal) is var s and >= 0 ? path.IndexOf('/', s + 3) : path.IndexOf(':');
        if (start < 0)
            return url;
        var parts = path[(start + 1)..].Trim('/').Split('/');
        return parts.Length >= 2 ? parts[^2] + "/" + parts[^1] : url;
    }

    private static string? FindGitDir(string start)
    {
        for (var dir = new DirectoryInfo(Path.GetFullPath(start)); dir is not null; dir = dir.Parent)
        {
            var dotGit = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(dotGit))
                return dotGit;
            // A worktree or submodule: ".git" is a file with "gitdir: <path>".
            if (File.Exists(dotGit) && File.ReadAllText(dotGit).Trim() is var text && text.StartsWith("gitdir:", StringComparison.Ordinal))
                return Path.GetFullPath(Path.Combine(dir.FullName, text["gitdir:".Length..].Trim()));
        }
        return null;
    }

    private static GitRepoInfo Read(string gitDir)
    {
        var commonFile = Path.Combine(gitDir, "commondir");
        var common = File.Exists(commonFile) ? Path.GetFullPath(Path.Combine(gitDir, File.ReadAllText(commonFile).Trim())) : gitDir;

        var headFile = Path.Combine(gitDir, "HEAD");
        var head = File.Exists(headFile) ? File.ReadAllText(headFile).Trim() : "";
        var current = head.StartsWith("ref: refs/heads/", StringComparison.Ordinal) ? head["ref: refs/heads/".Length..] : null;

        var urls = new Dictionary<string, string>(StringComparer.Ordinal);
        var branchRemotes = new Dictionary<string, string>(StringComparer.Ordinal);
        var configFile = Path.Combine(common, "config");
        if (File.Exists(configFile))
            ReadConfig(File.ReadAllLines(configFile), urls, branchRemotes);

        var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
        var packed = Path.Combine(common, "packed-refs") is var p && File.Exists(p) ? File.ReadAllLines(p) : [];
        foreach (var remote in urls.Keys)
        {
            var symbolic = Path.Combine(common, "refs", "remotes", remote, "HEAD");
            var text = File.Exists(symbolic) ? File.ReadAllText(symbolic).Trim() : "";
            var prefix = $"ref: refs/remotes/{remote}/";
            if (text.StartsWith(prefix, StringComparison.Ordinal))
                defaults[remote] = text[prefix.Length..];
            else if (new[] { "main", "master" }.FirstOrDefault(b => RefExists(common, packed, $"refs/remotes/{remote}/{b}")) is { } guess)
                defaults[remote] = guess;
        }
        return new GitRepoInfo(current, urls, defaults, branchRemotes);
    }

    private static bool RefExists(string common, string[] packed, string refName) =>
        File.Exists(Path.Combine(common, refName.Replace('/', Path.DirectorySeparatorChar)))
        || packed.Any(l => l.EndsWith(" " + refName, StringComparison.Ordinal));

    /// <summary>A git config value: quotes removed, and \\ \" \t \n turned back into their characters.</summary>
    private static string Unescape(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '"')
                continue;
            if (c == '\\' && i + 1 < value.Length)
            {
                var next = value[++i];
                sb.Append(next switch { 't' => '\t', 'n' => '\n', _ => next });
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>[remote "x"] url = ... and [branch "y"] remote = ...; other keys are not needed.</summary>
    private static void ReadConfig(string[] lines, Dictionary<string, string> urls, Dictionary<string, string> branchRemotes)
    {
        string? section = null, name = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                var end = line.IndexOf(']');
                var head = end > 0 ? line[1..end] : line[1..];
                var quote = head.IndexOf('"');
                section = (quote > 0 ? head[..quote] : head).Trim().ToLowerInvariant();
                name = quote > 0 ? head[(quote + 1)..].TrimEnd('"') : null;
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq <= 0 || name is null)
                continue;
            var key = line[..eq].Trim().ToLowerInvariant();
            var value = Unescape(line[(eq + 1)..].Trim());
            if (section == "remote" && key == "url")
                urls.TryAdd(name, value);
            else if (section == "branch" && key == "remote")
                branchRemotes[name] = value;
        }
    }
}
