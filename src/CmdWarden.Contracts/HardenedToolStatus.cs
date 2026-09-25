namespace CmdWarden.Contracts;

/// <summary>First Catalog tools, in display order (CONTEXT.md: First Catalog).</summary>
public static class ToolCatalog
{
    public sealed record Entry(string Id, string DisplayName);

    public static IReadOnlyList<Entry> Tools { get; } =
    [
        new("gh", "GitHub CLI"),
        new("git", "Git"),
        new("az", "Azure CLI"),
        new("docker", "Docker CLI"),
    ];
}

public enum HardenState
{
    NotHardened,
    Degraded,
    Hardened,
}

/// <summary>One registry PATH entry. <paramref name="Scope"/> is "machine" or "user".</summary>
public sealed record PathEntry(string Dir, string Scope);

/// <summary>
/// Read-only harden status for one tool: pin check + shim exe + shims dir first on PATH (issue #116, #201).
/// <paramref name="Note"/> carries mode detail, e.g. "strong - 3 registries in vault" (#204).
/// </summary>
public sealed record HardenedToolStatus(string Tool, HardenState State, string? PinnedPath, string? Reason, string? Note = null)
{
    public const string DockerCredsStoreDrift = "credsStore drift; run cw harden docker --strong";
    public const string DockerLegacyReturned = "legacy docker credentials returned; run cw harden docker --strong";
    public const string ShimNotFirstPrefix = "shim not first on PATH: ";
    public const string StaleProcessPathInfo = "this terminal started before the last PATH change; open a new terminal";
    public const string GitHelperDrift = "helper chain drift; run cw harden git --strong";
    public const string GitGhBlockReturned = "gh git helper returned; run cw harden git --strong";
    public const string GitLegacyReturned = "legacy git credentials returned; run cw harden git --strong";
    public const string GhStockReturned = "stock gh token returned; run cw harden gh --strong";
    public const string AzStockReturned = "stock az login returned; run cw harden az --strong";
    public const string GhNoVaultTokenPrefix = "no vault token for ";

    /// <param name="path">Composed registry PATH. Null reads the registry (machine entries, then user).</param>
    public static HardenedToolStatus Probe(
        string toolId,
        string? productRoot = null,
        IReadOnlyList<PathEntry>? path = null,
        string? dockerConfigPath = null,
        string? gitGlobalConfigPath = null)
    {
        var root = productRoot ?? ProductPaths.Root();
        var shimsDir = Path.Combine(root, "shims");
        var pin = new ToolPinStore(root).Check(toolId);
        var shimPresent = File.Exists(Path.Combine(shimsDir, toolId.Trim().ToLowerInvariant() + ".exe"));
        path ??= ReadRegistryPath();
        var onPath = path.Any(e => SameDir(e.Dir, shimsDir));

        if (pin.IsMissing && !shimPresent)
            return new(toolId, HardenState.NotHardened, null, null);

        var pinnedPath = pin.Pin?.Path ?? new ToolPinStore(root).TryGet(toolId)?.Path;
        if (pin.IsOk && shimPresent && onPath)
        {
            if (FirstBlocker(path, shimsDir, toolId) is { } blocker)
                return new(toolId, HardenState.Degraded, pinnedPath,
                    $"{ShimNotFirstPrefix}{blocker.Dir} ({blocker.Scope}) precedes shims; run cw doctor --fix-path");
            if (pin.Pin!.IsStrong && string.Equals(pin.Pin.Tool, "docker", StringComparison.OrdinalIgnoreCase)
                && OperatingSystem.IsWindows())
                return ProbeDockerStrong(toolId, pinnedPath, dockerConfigPath ?? DockerConfigFile.DefaultPath());
            if (pin.Pin.IsStrong && string.Equals(pin.Pin.Tool, "git", StringComparison.OrdinalIgnoreCase)
                && OperatingSystem.IsWindows())
                return ProbeGitStrong(toolId, pin.Pin, Path.Combine(shimsDir, HelperTools.GitHelperExe), gitGlobalConfigPath);
            if (pin.Pin.IsStrong && string.Equals(pin.Pin.Tool, "az", StringComparison.OrdinalIgnoreCase))
                return ProbeAzStrong(toolId, pinnedPath, root);
            if (pin.Pin.IsStrong && string.Equals(pin.Pin.Tool, "gh", StringComparison.OrdinalIgnoreCase)
                && OperatingSystem.IsWindows())
                return ProbeGhStrong(toolId, pinnedPath);
            return new(toolId, HardenState.Hardened, pinnedPath, null);
        }

        var reason = pin.IsMissing ? $"shim installed but no pin; run cw harden {toolId}"
            : !pin.IsOk ? pin.Error ?? "pin check failed"
            : !shimPresent ? "shim not found in " + shimsDir
            : "shims dir not on PATH";
        return new(toolId, HardenState.Degraded, pinnedPath, reason);
    }

    /// <summary>Machine entries (expanded) then user entries: the order Windows gives a new process.</summary>
    public static IReadOnlyList<PathEntry> ComposePath(string? machinePath, string? userPath)
    {
        var list = new List<PathEntry>();
        foreach (var dir in Split(Environment.ExpandEnvironmentVariables(machinePath ?? "")))
            list.Add(new(dir, "machine"));
        foreach (var dir in Split(userPath ?? ""))
            list.Add(new(dir, "user"));
        return list;
    }

    public static IReadOnlyList<PathEntry> ReadRegistryPath() => ComposePath(
        Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
        Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User));

    /// <summary>
    /// The first entry before the shims dir that holds tool.exe, tool.cmd, or tool.bat. Null when the
    /// shim wins. An earlier entry without the tool file does not count.
    /// </summary>
    public static PathEntry? FirstBlocker(IReadOnlyList<PathEntry> path, string shimsDir, string toolId)
    {
        var tool = toolId.Trim().ToLowerInvariant();
        foreach (var entry in path)
        {
            if (SameDir(entry.Dir, shimsDir))
                return null;
            if (new[] { ".exe", ".cmd", ".bat" }.Any(ext => File.Exists(Path.Combine(entry.Dir, tool + ext))))
                return entry;
        }
        return null;
    }

    /// <summary>
    /// True when the registry entries do not appear in the process PATH in the same order. A shell
    /// that adds its own entries is not stale; one that started before a PATH change is.
    /// </summary>
    public static bool IsProcessPathStale(IReadOnlyList<PathEntry> registry, string? processPath)
    {
        var process = Split(processPath ?? "").Select(Norm).ToList();
        var next = 0;
        foreach (var entry in registry)
        {
            var dir = Norm(entry.Dir);
            var at = process.FindIndex(next, p => string.Equals(p, dir, StringComparison.OrdinalIgnoreCase));
            if (at < 0)
                return true;
            next = at + 1;
        }
        return false;
    }

    private static string[] Split(string pathValue) =>
        pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool SameDir(string a, string b) =>
        string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Strong docker: config must name cmdwarden and no Docker-labeled CredMan entry may exist.</summary>
    private static HardenedToolStatus ProbeDockerStrong(string toolId, string? pinnedPath, string configPath)
    {
        var vault = new CredentialVault();
        var config = DockerConfigFile.Read(configPath);
        if (!string.Equals(config["credsStore"]?.GetValue<string>(), DockerConfigFile.CmdWardenStore, StringComparison.Ordinal))
            return new(toolId, HardenState.Degraded, pinnedPath, DockerCredsStoreDrift);
        if (vault.ReadAllWithLabel(DockerConfigFile.LegacyLabel).Any(e => !DockerConfigFile.IsForeignHelperTarget(config, e.Target)))
            return new(toolId, HardenState.Degraded, pinnedPath, DockerLegacyReturned);
        var count = vault.ListTargets(VaultNames.HelperTargetPrefix("docker")).Count;
        return new(toolId, HardenState.Hardened, pinnedPath, null, $"strong - {count} registries in vault");
    }

    /// <summary>
    /// Strong git (#207): the global helper list is exactly ["", helper], no gh host block, and no
    /// legacy entry under the GCM namespace.
    /// </summary>
    private static HardenedToolStatus ProbeGitStrong(string toolId, ToolPin pin, string helperExe, string? globalConfigPath)
    {
        var config = new GitGlobalConfig(pin.Path, globalConfigPath);
        var helpers = config.GetAll(GitGlobalConfig.HelperKey);
        if (helpers.Count != 2 || helpers[0].Length != 0
            || !helpers[1].Equals(GitGlobalConfig.ShPath(helperExe), StringComparison.OrdinalIgnoreCase))
            return new(toolId, HardenState.Degraded, pin.Path, GitHelperDrift);
        if (config.GetScopedHelpers().Any(l => GitGlobalConfig.IsGhHelperValue(l.Value)))
            return new(toolId, HardenState.Degraded, pin.Path, GitGhBlockReturned);

        var vault = new CredentialVault();
        var ns = config.Get("credential.namespace");
        if (string.IsNullOrWhiteSpace(ns))
            ns = "git";
        if (vault.ListTargets(ns.Trim() + ":").Count > 0)
            return new(toolId, HardenState.Degraded, pin.Path, GitLegacyReturned);

        var hosts = vault.ListTargets(GitVaultNames.Prefix)
            .Select(t => t.Target[GitVaultNames.Prefix.Length..])
            .Select(k => { try { return GitVaultNames.Parse(k).Host; } catch (ArgumentException) { return null; } })
            .Where(h => h is not null && !h.StartsWith("oauth-refresh-token.", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        return new(toolId, HardenState.Hardened, pin.Path, null, $"strong - {hosts} git hosts in vault");
    }

    /// <summary>
    /// Strong gh (#209): no stock gh:&lt;host&gt;:* entry, no oauth_token line, and a vault token for
    /// the active user of every host in hosts.yml.
    /// </summary>
    private static HardenedToolStatus ProbeGhStrong(string toolId, string? pinnedPath)
    {
        var store = new GhStrongStore();
        var hosts = store.Hosts();
        if (new CredentialVault().ListTargets(store.StockPrefix).Count > 0 || hosts.Any(h => h.Tokens.Count > 0))
            return new(toolId, HardenState.Degraded, pinnedPath, GhStockReturned);
        var keys = store.Keys();
        foreach (var h in hosts)
        {
            if (h.ActiveUser is { Length: > 0 } user && !keys.Contains((user, h.Host)))
                return new(toolId, HardenState.Degraded, pinnedPath, $"{GhNoVaultTokenPrefix}{h.Host}; run gh auth login");
        }
        var hostCount = keys.Select(k => k.Host).Distinct().Count();
        var accountCount = keys.Count(k => k.User.Length > 0);
        return new(toolId, HardenState.Hardened, pinnedPath, null, $"strong - {hostCount} hosts, {accountCount} accounts in vault");
    }

    /// <summary>Strong az (#26): no login file back in the stock config dir.</summary>
    private static HardenedToolStatus ProbeAzStrong(string toolId, string? pinnedPath, string? productRoot)
    {
        var store = new AzStrongStore(productRoot);
        if (store.StockHasLogin())
            return new(toolId, HardenState.Degraded, pinnedPath, AzStockReturned);
        var hasLogin = store.Load().Keys.Any(AzStrongStore.LoginFiles.Contains);
        return new(toolId, HardenState.Hardened, pinnedPath, null, hasLogin ? "strong - az login in the store" : "strong - no az login yet; run az login");
    }

    private static string Norm(string p)
    {
        try { return Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return p; }
    }
}
