namespace CmdWarden.Contracts;

/// <summary>Effective level for one catalog tool on one launcher; IsOverride when set on the launcher, not inherited.</summary>
public sealed record PolicyToolLevel(string Tool, PolicyLevel Level, bool IsOverride);

/// <summary>One enrolled launcher with the effective level per First Catalog tool.</summary>
public sealed record PolicyLauncherEntry(
    string PolicyKey, LauncherEnrollmentKind Kind, string? DisplayPath, IReadOnlyList<PolicyToolLevel> Tools);

/// <summary>
/// Read-only view of the policy file for the Secret Gates tab (issue #119).
/// Same path as cw policy path; a malformed file throws instead of falling back to defaults.
/// </summary>
public sealed record PolicyReadModel(
    string Path, PolicyLevel AiHarnessDefault, PolicyLevel TerminalDefault, IReadOnlyList<PolicyLauncherEntry> Launchers)
{
    public static PolicyReadModel Load(string? path = null)
    {
        var store = new PolicyStore(path ?? PolicyStore.DefaultPath());
        store.Load();

        var catalog = ToolCatalog.All();
        var launchers = store.Launchers
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv =>
            {
                LauncherEnrollmentKindNames.TryParse(kv.Value.Kind, out var kind);
                var levels = kv.Value.Levels;
                var tools = catalog.Select(t =>
                {
                    // ponytail: level comes from the evaluator's own resolver so the tab can never disagree with the agent.
                    var level = store.ResolveLevel(t.Id, kv.Key, autoApproveEligible: true).Level;
                    var isOverride = levels is not null
                        && ((levels.TryGetValue(t.Id, out var raw) && PolicyLevelNames.TryParse(raw, out _))
                            || (levels.TryGetValue("*", out var wild) && PolicyLevelNames.TryParse(wild, out _)));
                    return new PolicyToolLevel(t.Id, level, isOverride);
                }).ToList();
                return new PolicyLauncherEntry(kv.Key, kind, kv.Value.Path, tools);
            })
            .ToList();

        return new PolicyReadModel(store.Path, store.DefaultAiHarnessLevel, store.DefaultTerminalLevel, launchers);
    }

    /// <summary>
    /// Launchers in the audit that are not enrolled, newest first, one row per key (#44).
    /// The Vault offers them in its Enroll dialog. Unknown launchers have no stable key and stay out.
    /// </summary>
    public static IReadOnlyList<SeenLauncher> RecentUnenrolled(
        IEnumerable<AuditGateRecord> newestFirst, IReadOnlyDictionary<string, LauncherEntryDto> enrolled)
    {
        var seen = new List<SeenLauncher>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in newestFirst)
        {
            var key = r.LauncherPolicyKey.Trim();
            if (key.Length == 0 || key.Equals(LauncherKinds.PolicyKeyUnknown, StringComparison.OrdinalIgnoreCase)
                || enrolled.ContainsKey(key) || !keys.Add(key))
                continue;
            seen.Add(new SeenLauncher(key, string.IsNullOrWhiteSpace(r.LauncherPath) ? null : r.LauncherPath, r.Timestamp));
        }
        return seen;
    }
}

/// <summary>A launcher that the audit saw, and that is not enrolled.</summary>
public sealed record SeenLauncher(string PolicyKey, string? Path, DateTimeOffset? LastSeen);
