using System.IO.Enumeration;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CmdWarden.Contracts;

/// <summary>
/// A tool described by one JSON file (#37). One generic shim gates it; no tool code is needed.
/// See docs/tool-packs.md for the schema.
/// </summary>
public sealed class ToolPack
{
    /// <summary>The command name on PATH, for example npm. The shim is &lt;tool&gt;.exe.</summary>
    public string Tool { get; init; } = "";
    public string DisplayName { get; init; } = "";
    /// <summary>Optional logo: SVG path data in a 24x24 box and a color, as in <see cref="BrandMark"/>.</summary>
    public ToolPackLogo? Logo { get; init; }
    /// <summary>File names of the real tool, tried in order in each PATH entry.</summary>
    public List<string> Binaries { get; init; } = [];
    /// <summary>Vault names that go into the child env as variables of the same name.</summary>
    public List<string> SecretEnv { get; init; } = [];
    /// <summary>Flags that take the next word as their value, so that word is not a command word.</summary>
    public List<string> FlagsWithValue { get; init; } = [];
    /// <summary>The class of a command that no rule matches.</summary>
    public string Default { get; init; } = CommandClassNames.Unknown;
    /// <summary>First match wins.</summary>
    public List<ToolPackRule> Rules { get; init; } = [];
    /// <summary>Config files that can hold a plain secret. cw scan reads them.</summary>
    public List<ToolPackSecretFile> SecretFiles { get; init; } = [];
    /// <summary>Sample commands and their class. A pack that fails a sample does not load.</summary>
    public List<ToolPackSample> Samples { get; init; } = [];

    /// <summary>"built-in", or the path of the user pack file.</summary>
    [JsonIgnore]
    public string Source { get; internal set; } = "";

    public string Name => string.IsNullOrWhiteSpace(DisplayName) ? Tool : DisplayName;

    private static readonly string[] HelpFlags = ["-h", "--help"];
    private static readonly string[] VersionFlags = ["-v", "-V", "--version"];

    public CommandClass Classify(IReadOnlyList<string> argv)
    {
        if (IsHelpOnly(argv))
            return CommandClass.Read;
        return Match(argv) is { } rule ? rule.CommandClass : CommandClassNames.ParseOrUnknown(Default);
    }

    /// <summary>Help or version only: the run gets no secret.</summary>
    public bool IsHelpOnly(IReadOnlyList<string> argv)
    {
        if (argv.Count == 0)
            return false;
        if (HelpFlags.Contains(argv[^1]) || argv.All(a => VersionFlags.Contains(a)))
            return true;
        return CommandWords(argv) is [var first, ..] && first.Equals("help", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the run gets the <see cref="SecretEnv"/> values that are in the vault.</summary>
    public bool ReleasesSecrets(IReadOnlyList<string> argv) =>
        SecretEnv.Count > 0 && !IsHelpOnly(argv) && Match(argv)?.Secrets != false;

    public RiskAssessment Assess(IReadOnlyList<string> argv)
    {
        var rule = IsHelpOnly(argv) ? null : Match(argv);
        return rule?.Risk?.ToLowerInvariant() switch
        {
            "high" => new(RiskLevel.High, $"Runs {Tool} {string.Join(' ', argv)}. {RiskAssessor.CannotUndo}"),
            "low" => new(RiskLevel.Low, null),
            _ => RiskAssessment.Plain,
        };
    }

    /// <summary>The first rule that matches, or null.</summary>
    public ToolPackRule? Match(IReadOnlyList<string> argv)
    {
        var words = CommandWords(argv);
        return Rules.FirstOrDefault(r => r.Matches(words, argv));
    }

    /// <summary>
    /// The words that are not flags, in order. The value after a flag in <see cref="FlagsWithValue"/>
    /// is skipped. Words after "--" belong to the child command and do not count.
    /// </summary>
    public List<string> CommandWords(IReadOnlyList<string> argv)
    {
        var words = new List<string>();
        for (var i = 0; i < argv.Count; i++)
        {
            var a = argv[i];
            if (a == "--")
                break;
            if (FlagsWithValue.Contains(a, StringComparer.OrdinalIgnoreCase))
                i++;
            else if (!a.StartsWith('-'))
                words.Add(a);
        }
        return words;
    }

    /// <summary>Problems that stop the pack from loading. Empty when the pack is good.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!ToolPacks.IsValidToolName(Tool))
            errors.Add($"tool '{Tool}' must be lower case letters, digits, '.', '-' or '_'.");
        else if (ToolCatalog.IsBuiltIn(Tool))
            errors.Add($"tool '{Tool}' is built in; a pack cannot replace it.");
        if (Binaries.Count == 0)
            errors.Add("binaries must name at least one file, for example npm.cmd.");
        foreach (var b in Binaries)
        {
            if (b != Path.GetFileName(b) || Path.GetExtension(b).ToLowerInvariant() is not (".exe" or ".cmd" or ".bat"))
                errors.Add($"binary '{b}' must be a file name that ends in .exe, .cmd or .bat.");
        }
        foreach (var name in SecretEnv)
        {
            try
            {
                VaultNames.TargetName(name);
            }
            catch (ArgumentException)
            {
                errors.Add($"secretEnv '{name}' is not a valid vault name.");
            }
        }
        if (Logo is not null && !Regex.IsMatch(Logo.Color, "^#[0-9A-Fa-f]{6}$"))
            errors.Add($"logo color '{Logo.Color}' must be #RRGGBB.");
        if (!CommandClassNames.TryParse(Default, out _))
            errors.Add($"default '{Default}' is not a class (read, write, secret-reveal, unknown).");
        foreach (var rule in Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Match))
                errors.Add("a rule has no match.");
            if (!CommandClassNames.TryParse(rule.Class, out _))
                errors.Add($"rule '{rule.Match}': class '{rule.Class}' is not a class.");
            if (rule.Risk is not null && rule.Risk.ToLowerInvariant() is not ("low" or "normal" or "high"))
                errors.Add($"rule '{rule.Match}': risk '{rule.Risk}' must be low, normal or high.");
        }
        foreach (var file in SecretFiles)
        {
            if (string.IsNullOrWhiteSpace(file.Path) || string.IsNullOrWhiteSpace(file.Title))
                errors.Add("a secretFiles entry needs a path and a title.");
            try
            {
                _ = new Regex(file.Pattern, RegexOptions.Multiline, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException ex)
            {
                errors.Add($"secretFiles '{file.Path}': bad pattern: {ex.Message}");
            }
        }
        if (errors.Count > 0)
            return errors;
        foreach (var sample in Samples)
        {
            var argv = sample.Argv.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var actual = CommandClassNames.Format(Classify(argv));
            if (actual != CommandClassNames.Format(CommandClassNames.ParseOrUnknown(sample.Class)))
                errors.Add($"sample '{Tool} {sample.Argv}' is {actual}, not {sample.Class}.");
        }
        return errors;
    }
}

/// <summary>
/// One class rule. <see cref="Match"/> is words and flags split by spaces. Words match the command
/// words in order from the first one; '*' and '?' are wildcards and 'a|b' is either. A flag must be
/// somewhere in the command, alone or as flag=value; '-a|-b' is either flag.
/// </summary>
public sealed class ToolPackRule
{
    public string Match { get; init; } = "";
    public string Class { get; init; } = CommandClassNames.Unknown;
    /// <summary>False: the run gets no secret, for example npm run, which starts any script.</summary>
    public bool Secrets { get; init; } = true;
    /// <summary>low, normal or high. High always shows the popup below Full.</summary>
    public string? Risk { get; init; }

    [JsonIgnore]
    public CommandClass CommandClass => CommandClassNames.ParseOrUnknown(Class);

    internal bool Matches(IReadOnlyList<string> words, IReadOnlyList<string> argv)
    {
        var tokens = Match.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var at = 0;
        foreach (var token in tokens)
        {
            if (token.StartsWith('-'))
            {
                if (!token.Split('|').Any(flag => argv.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase)
                        || a.StartsWith(flag + "=", StringComparison.OrdinalIgnoreCase))))
                    return false;
                continue;
            }
            if (at >= words.Count || !token.Split('|').Any(alt => FileSystemName.MatchesSimpleExpression(alt, words[at], ignoreCase: true)))
                return false;
            at++;
        }
        return true;
    }
}

/// <summary>
/// A config file that can hold a plain secret. <see cref="Path"/> starts with ~ for the user
/// profile, or is relative to the working folder. A line that matches <see cref="Pattern"/> is a finding.
/// </summary>
public sealed class ToolPackSecretFile
{
    public string Path { get; init; } = "";
    public string Pattern { get; init; } = "";
    public string Title { get; init; } = "";
    public string? Remediation { get; init; }
}

public sealed class ToolPackLogo
{
    public string Path { get; init; } = "";
    public string Color { get; init; } = "#F3F3F3";
}

public sealed class ToolPackSample
{
    public string Argv { get; init; } = "";
    public string Class { get; init; } = "";
}

/// <summary>A pack file that did not load, and why.</summary>
public sealed record ToolPackError(string Source, string Message);

/// <summary>
/// The built-in packs (embedded in this assembly) and the user packs in &lt;product root&gt;/packs.
/// A user pack with the same tool as a built-in pack replaces it.
/// </summary>
public static class ToolPacks
{
    public const string BuiltInSource = "built-in";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly Lazy<IReadOnlyList<ToolPack>> BuiltIns = new(LoadBuiltIns);

    public static IReadOnlyList<ToolPack> BuiltIn => BuiltIns.Value;

    public static string UserDir(string? productRoot = null) => Path.Combine(productRoot ?? ProductPaths.Root(), "packs");

    public static bool IsValidToolName(string? tool) =>
        tool is { Length: > 0 and <= 64 }
        && (char.IsAsciiLetterLower(tool[0]) || char.IsAsciiDigit(tool[0]))
        && tool.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '-' or '_');

    /// <summary>Every pack that loads, sorted by tool, and the files that did not load.</summary>
    public static (IReadOnlyList<ToolPack> Packs, IReadOnlyList<ToolPackError> Errors) Load(string? productRoot = null)
    {
        var packs = BuiltIn.ToDictionary(p => p.Tool, StringComparer.OrdinalIgnoreCase);
        var errors = new List<ToolPackError>();
        var dir = UserDir(productRoot);
        if (Directory.Exists(dir))
        {
            foreach (var file in Directory.GetFiles(dir, "*.json").Order(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var pack = Parse(File.ReadAllText(file), file);
                    packs[pack.Tool] = pack;
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    errors.Add(new ToolPackError(file, ex.Message));
                }
            }
        }
        return (packs.Values.OrderBy(p => p.Tool, StringComparer.Ordinal).ToList(), errors);
    }

    public static ToolPack? Find(string tool, string? productRoot = null) =>
        ToolCatalog.IsBuiltIn(tool) ? null
            : Load(productRoot).Packs.FirstOrDefault(p => p.Tool.Equals(tool, StringComparison.OrdinalIgnoreCase));

    /// <summary>Read and check one pack. Throws <see cref="InvalidDataException"/> with every problem.</summary>
    public static ToolPack Parse(string json, string source)
    {
        ToolPack? pack;
        try
        {
            pack = JsonSerializer.Deserialize<ToolPack>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{source}: not valid JSON: {ex.Message}");
        }
        if (pack is null)
            throw new InvalidDataException($"{source}: empty pack.");
        var errors = pack.Validate();
        if (errors.Count > 0)
            throw new InvalidDataException($"{source}: {string.Join(" ", errors)}");
        pack.Source = source;
        return pack;
    }

    private static IReadOnlyList<ToolPack> LoadBuiltIns()
    {
        var assembly = typeof(ToolPacks).Assembly;
        var packs = new List<ToolPack>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".pack.json", StringComparison.Ordinal)).Order())
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            packs.Add(Parse(reader.ReadToEnd(), BuiltInSource));
        }
        return packs;
    }
}
