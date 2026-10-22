using System.Text.Json;

namespace CmdWarden.Contracts;

/// <summary>
/// #47: team rules from a shared git repo. A team rule is a maximum level for one enrollment kind
/// and one tool ("*" for every tool). It can only lower a level, so a changed repo cannot open the gate.
/// Local enrollments stay local. <c>cw policy sync</c> writes this file next to policy.json.
/// </summary>
public sealed class TeamPolicy
{
    public const string DefaultRepoFile = "cmdwarden-team.json";
    public const string FileName = "team-policy.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Kind (ai_harness or terminal), then tool or "*", then level name.</summary>
    public Dictionary<string, Dictionary<string, string>> MaxLevels { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Source { get; set; }
    public string? Ref { get; set; }
    public string? File { get; set; }
    public string? Commit { get; set; }
    public string? SyncedAtUtc { get; set; }

    public static string PathFor(string policyPath) =>
        Path.Combine(Path.GetDirectoryName(policyPath) ?? "", FileName);

    /// <summary>Team file text to rules. Throws <see cref="InvalidDataException"/> for an unknown kind, level, or field.</summary>
    public static TeamPolicy Parse(string json)
    {
        TeamPolicy? raw;
        try
        {
            raw = JsonSerializer.Deserialize<TeamPolicy>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The team file is not valid JSON: " + ex.Message, ex);
        }
        var team = raw ?? new TeamPolicy();
        var caps = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (kindName, tools) in team.MaxLevels ?? new())
        {
            if (!LauncherEnrollmentKindNames.TryParse(kindName, out var kind) || kind == LauncherEnrollmentKind.Unknown)
                throw new InvalidDataException($"Unknown launcher kind '{kindName}' in maxLevels. Use ai-harness or terminal.");
            var levels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (tool, levelName) in tools ?? new())
            {
                if (string.IsNullOrWhiteSpace(tool) || !PolicyLevelNames.TryParse(levelName, out var level))
                    throw new InvalidDataException($"Unknown level '{levelName}' for {kindName} {tool}. Use Deny, Read, Trusted, or Full.");
                levels[tool.Trim()] = PolicyLevelNames.Format(level);
            }
            caps[LauncherEnrollmentKindNames.Format(kind)] = levels;
        }
        team.MaxLevels = caps;
        return team;
    }

    /// <summary>The saved team rules, or null when there are none. A damaged file throws, so the gate fails closed.</summary>
    public static TeamPolicy? Load(string path)
    {
        if (!System.IO.File.Exists(path))
            return null;
        try
        {
            return Parse(System.IO.File.ReadAllText(path));
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException(
                $"The team rules file {path} is damaged. {ex.Message} Run cw policy sync again, or cw policy sync --remove.", ex);
        }
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>The team maximum for a kind and tool: the tool rule first, then "*". Null when the team sets none.</summary>
    public PolicyLevel? CapFor(LauncherEnrollmentKind kind, string tool)
    {
        if (!MaxLevels.TryGetValue(LauncherEnrollmentKindNames.Format(kind), out var levels))
            return null;
        if (levels.TryGetValue(tool, out var named) || levels.TryGetValue("*", out named))
            return PolicyLevelNames.ParseOrDeny(named);
        return null;
    }

    /// <summary>One line per rule, sorted, for display and to see if the rules changed.</summary>
    public IReadOnlyList<string> Lines() =>
        MaxLevels.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
            .SelectMany(k => k.Value.OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
                .Select(t => $"{k.Key} {t.Key}: max {t.Value}"))
            .ToList();
}
