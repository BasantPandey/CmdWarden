using System.Text.Json;
using System.Text.Json.Serialization;

namespace CmdWarden.Contracts;

/// <summary>
/// Spike policy store: LocalAppData JSON enrollment of launchers and per-tool levels.
/// </summary>
public sealed class PolicyStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _path;
    private PolicyDocument _doc = PolicyDocument.CreateDefault();

    public PolicyStore(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public string Path => _path;

    public PolicyLevel DefaultAiHarnessLevel =>
        PolicyLevelNames.ParseOrDeny(_doc.Defaults.AiHarness);

    public PolicyLevel DefaultTerminalLevel =>
        PolicyLevelNames.ParseOrDeny(_doc.Defaults.Terminal);

    /// <summary>When the Approval Gate asks for Windows Hello (#24): off, secret-reveal, or write-and-up.</summary>
    public string HelloMode =>
        WindowsHelloPolicy.TryParse(_doc.Hello, out var mode) ? mode : WindowsHelloPolicy.Default;

    public void SetHelloMode(string mode)
    {
        if (!WindowsHelloPolicy.TryParse(mode, out var parsed))
            throw new ArgumentException($"Unknown Hello mode '{mode}'. Use {string.Join(", ", WindowsHelloPolicy.Modes)}.", nameof(mode));
        _doc.Hello = parsed;
    }

    /// <summary>#35: "allow" auto-allows a write that the risk check marks low risk, for an enrolled launcher. Default "ask".</summary>
    public bool LowRiskWritesAllowed => string.Equals(_doc.LowRisk, LowRiskModes.Allow, StringComparison.OrdinalIgnoreCase);

    public void SetLowRisk(string mode)
    {
        if (mode is not (LowRiskModes.Allow or LowRiskModes.Ask))
            throw new ArgumentException($"Unknown low-risk mode '{mode}'. Use {LowRiskModes.Allow} or {LowRiskModes.Ask}.", nameof(mode));
        _doc.LowRisk = mode;
    }

    public static PolicyStore CreateEmpty()
    {
        var store = new PolicyStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cw-empty-policy.json"));
        store._doc = PolicyDocument.CreateDefault();
        return store;
    }

    public static string DefaultPath()
    {
        var root = Environment.GetEnvironmentVariable("CW_POLICY_PATH");
        if (!string.IsNullOrWhiteSpace(root))
        {
            // Allow either a file path or a directory.
            if (root.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return root;
            return System.IO.Path.Combine(root, "policy.json");
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return System.IO.Path.Combine(local, "CmdWarden", "policy.json");
    }

    /// <summary>
    /// Reload from disk. Returns what changed since the last load (#133), so a long-lived caller
    /// (the Session Agent) can drop stale approval memory without polling the file itself.
    /// </summary>
    public IReadOnlyList<PolicyChange> Load()
    {
        var previous = _doc;

        if (!File.Exists(_path))
        {
            _doc = PolicyDocument.CreateDefault();
            return DiffLaunchers(previous.Launchers, _doc.Launchers);
        }

        var json = File.ReadAllText(_path);
        if (string.IsNullOrWhiteSpace(json))
        {
            _doc = PolicyDocument.CreateDefault();
            return DiffLaunchers(previous.Launchers, _doc.Launchers);
        }

        _doc = JsonSerializer.Deserialize<PolicyDocument>(json, JsonOptions) ?? PolicyDocument.CreateDefault();
        _doc.Defaults ??= new PolicyDefaultsDto();
        // Re-key with ordinal-ignore-case so lookups match enrollment/runtime keys.
        var launchers = new Dictionary<string, LauncherEntryDto>(StringComparer.OrdinalIgnoreCase);
        if (_doc.Launchers is not null)
        {
            foreach (var (key, entry) in _doc.Launchers)
            {
                if (entry.Levels is not null)
                {
                    entry.Levels = new Dictionary<string, string>(
                        entry.Levels,
                        StringComparer.OrdinalIgnoreCase);
                }

                launchers[key] = entry;
            }
        }

        _doc.Launchers = launchers;
        return DiffLaunchers(previous.Launchers, _doc.Launchers);
    }

    /// <summary>One launcher's enrollment vanished or changed kind (<see cref="Tool"/> null, e.g. unenroll) or one
    /// tool's level changed under it (e.g. policy set).</summary>
    public readonly record struct PolicyChange(string LauncherPolicyKey, string? Tool);

    private static IReadOnlyList<PolicyChange> DiffLaunchers(
        IReadOnlyDictionary<string, LauncherEntryDto> before,
        IReadOnlyDictionary<string, LauncherEntryDto> after)
    {
        List<PolicyChange>? changes = null;
        foreach (var (key, oldEntry) in before)
        {
            // A new kind changes the defaults and the session offer, so it drops the memory like an unenroll.
            if (!after.TryGetValue(key, out var newEntry)
                || !string.Equals(oldEntry.Kind, newEntry.Kind, StringComparison.OrdinalIgnoreCase))
            {
                (changes ??= new()).Add(new PolicyChange(key, null));
                continue;
            }

            var oldLevels = oldEntry.Levels;
            var newLevels = newEntry.Levels;
            var tools = (oldLevels?.Keys ?? Enumerable.Empty<string>())
                .Concat(newLevels?.Keys ?? Enumerable.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var tool in tools)
            {
                string? oldLevel = null;
                string? newLevel = null;
                var hasOld = oldLevels?.TryGetValue(tool, out oldLevel) ?? false;
                var hasNew = newLevels?.TryGetValue(tool, out newLevel) ?? false;
                if (hasOld != hasNew || !string.Equals(oldLevel, newLevel, StringComparison.OrdinalIgnoreCase))
                    (changes ??= new()).Add(new PolicyChange(key, tool));
            }
        }

        return (IReadOnlyList<PolicyChange>?)changes ?? Array.Empty<PolicyChange>();
    }

    public void Save()
    {
        var dir = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(_doc, JsonOptions);
        File.WriteAllText(_path, json);
    }

    public void SetDefaults(PolicyLevel aiHarness, PolicyLevel terminal)
    {
        _doc.Defaults.AiHarness = PolicyLevelNames.Format(aiHarness);
        _doc.Defaults.Terminal = PolicyLevelNames.Format(terminal);
    }

    public void Enroll(string policyKey, LauncherEnrollmentKind kind, string? displayPath = null)
    {
        if (string.IsNullOrWhiteSpace(policyKey))
            throw new ArgumentException("Policy key is required.", nameof(policyKey));
        if (kind == LauncherEnrollmentKind.Unknown)
            throw new ArgumentException("Cannot enroll as Unknown.", nameof(kind));

        var key = policyKey.Trim();
        if (!_doc.Launchers.TryGetValue(key, out var entry))
        {
            entry = new LauncherEntryDto();
            _doc.Launchers[key] = entry;
        }

        entry.Kind = LauncherEnrollmentKindNames.Format(kind);
        if (!string.IsNullOrWhiteSpace(displayPath))
            entry.Path = displayPath.Trim();
        entry.Levels ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public void SetLevel(string policyKey, string tool, PolicyLevel level)
    {
        if (string.IsNullOrWhiteSpace(policyKey))
            throw new ArgumentException("Policy key is required.", nameof(policyKey));
        if (string.IsNullOrWhiteSpace(tool))
            throw new ArgumentException("Tool is required.", nameof(tool));

        var key = policyKey.Trim();
        if (!_doc.Launchers.TryGetValue(key, out var entry))
        {
            entry = new LauncherEntryDto
            {
                Kind = LauncherEnrollmentKindNames.Terminal,
                Levels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            };
            _doc.Launchers[key] = entry;
        }

        entry.Levels ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        entry.Levels[tool.Trim()] = PolicyLevelNames.Format(level);
    }

    public bool Unenroll(string policyKey) => _doc.Launchers.Remove(policyKey.Trim());

    public IReadOnlyDictionary<string, LauncherEntryDto> Launchers => _doc.Launchers;

    /// <summary>
    /// Resolve the effective Policy Level for tool × launcher.
    /// Unknown / ineligible / unenrolled → Deny (never auto-allow).
    /// </summary>
    public PolicyResolveResult ResolveLevel(string tool, string launcherPolicyKey, bool autoApproveEligible)
    {
        tool = string.IsNullOrWhiteSpace(tool) ? "*" : tool.Trim();
        var key = string.IsNullOrWhiteSpace(launcherPolicyKey)
            ? LauncherKinds.PolicyKeyUnknown
            : launcherPolicyKey.Trim();

        if (!autoApproveEligible
            || string.Equals(key, LauncherKinds.PolicyKeyUnknown, StringComparison.OrdinalIgnoreCase)
            || key.Equals("unknown", StringComparison.OrdinalIgnoreCase))
        {
            return new PolicyResolveResult
            {
                Level = PolicyLevel.Deny,
                IsEnrolled = false,
                EnrollmentKind = LauncherEnrollmentKind.Unknown,
                ReasonCode = PolicyReasonCodes.UnknownLauncher,
                Tool = tool,
                LauncherPolicyKey = key,
            };
        }

        if (!_doc.Launchers.TryGetValue(key, out var entry))
        {
            return new PolicyResolveResult
            {
                Level = PolicyLevel.Deny,
                IsEnrolled = false,
                EnrollmentKind = LauncherEnrollmentKind.Unknown,
                ReasonCode = PolicyReasonCodes.NotEnrolled,
                Tool = tool,
                LauncherPolicyKey = key,
            };
        }

        LauncherEnrollmentKindNames.TryParse(entry.Kind, out var kind);
        if (kind == LauncherEnrollmentKind.Unknown)
            kind = LauncherEnrollmentKind.Terminal;

        PolicyLevel level;
        if (entry.Levels is not null
            && entry.Levels.TryGetValue(tool, out var explicitLevel)
            && PolicyLevelNames.TryParse(explicitLevel, out var parsedExplicit))
        {
            level = parsedExplicit;
        }
        else if (entry.Levels is not null
            && entry.Levels.TryGetValue("*", out var wildcard)
            && PolicyLevelNames.TryParse(wildcard, out var parsedWild))
        {
            level = parsedWild;
        }
        else
        {
            level = kind == LauncherEnrollmentKind.AiHarness
                ? DefaultAiHarnessLevel
                : DefaultTerminalLevel;
        }

        return new PolicyResolveResult
        {
            Level = level,
            IsEnrolled = true,
            EnrollmentKind = kind,
            ReasonCode = null,
            Tool = tool,
            LauncherPolicyKey = key,
        };
    }
}

public sealed class PolicyResolveResult
{
    public required PolicyLevel Level { get; init; }
    public required bool IsEnrolled { get; init; }
    public required LauncherEnrollmentKind EnrollmentKind { get; init; }
    public string? ReasonCode { get; init; }
    public required string Tool { get; init; }
    public required string LauncherPolicyKey { get; init; }
}

internal sealed class PolicyDocument
{
    public PolicyDefaultsDto Defaults { get; set; } = new();
    public string Hello { get; set; } = WindowsHelloPolicy.Default;
    public string LowRisk { get; set; } = LowRiskModes.Ask;
    public Dictionary<string, LauncherEntryDto> Launchers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public static PolicyDocument CreateDefault() => new()
    {
        Defaults = new PolicyDefaultsDto
        {
            AiHarness = PolicyLevelNames.Read,
            Terminal = PolicyLevelNames.Trusted,
        },
        Launchers = new Dictionary<string, LauncherEntryDto>(StringComparer.OrdinalIgnoreCase),
    };
}

internal sealed class PolicyDefaultsDto
{
    public string AiHarness { get; set; } = PolicyLevelNames.Read;
    public string Terminal { get; set; } = PolicyLevelNames.Trusted;
}

public sealed class LauncherEntryDto
{
    public string Kind { get; set; } = LauncherEnrollmentKindNames.Terminal;

    /// <summary>Display path recorded at enroll for list UX (spec section 6); not identity.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; set; }

    public Dictionary<string, string>? Levels { get; set; }
}

/// <summary>#35: what policy does with a low-risk write that the level does not auto-allow.</summary>
public static class LowRiskModes
{
    public const string Ask = "ask";
    public const string Allow = "allow";
}
