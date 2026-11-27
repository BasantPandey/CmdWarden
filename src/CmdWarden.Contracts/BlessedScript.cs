using System.Text.Json;
using System.Text.Json.Serialization;

namespace CmdWarden.Contracts;

/// <summary>
/// One reviewed script (issue 201). The file hash is the review.
/// A later run of that exact file may use the tool caps. A secret reveal never may.
/// </summary>
public sealed class ScriptBlessing
{
    public required string Path { get; init; }
    public required string Sha256 { get; init; }
    public required Dictionary<string, string> Caps { get; init; }

    /// <summary>
    /// True when this file still has the reviewed hash and the cap covers the class.
    /// Secret reveal and unknown never match.
    /// </summary>
    public bool Covers(string currentHash, string tool, CommandClass commandClass)
    {
        if (commandClass is CommandClass.SecretReveal or CommandClass.Unknown)
            return false;
        if (!string.Equals(Sha256, currentHash, StringComparison.OrdinalIgnoreCase))
            return false;
        if (Caps is null || !Caps.TryGetValue(tool, out var cap))
            return false;
        return PolicyEvaluator.IsAutoAllowed(CapLevel(cap), commandClass);
    }

    /// <summary>read stays Read. write is Trusted: read and write, not a secret reveal.</summary>
    public static PolicyLevel CapLevel(string? cap) => cap?.Trim().ToLowerInvariant() switch
    {
        "read" => PolicyLevel.Read,
        "write" => PolicyLevel.Trusted,
        _ => PolicyLevel.Deny,
    };
}

/// <summary>One process command line above the gated tool.</summary>
public readonly record struct AncestorCommand(string Exe, IReadOnlyList<string> Args);

/// <summary>
/// Decides whether a reviewed script in the caller chain covers this tool call.
/// </summary>
public static class BlessedScriptMatch
{
    public static bool Allows(
        IReadOnlyList<ScriptBlessing> blessings,
        IEnumerable<AncestorCommand> ancestors,
        string tool,
        CommandClass commandClass,
        Func<string, string?> hashOf)
    {
        if (blessings.Count == 0 || commandClass is CommandClass.SecretReveal or CommandClass.Unknown)
            return false;

        foreach (var ancestor in ancestors)
        {
            var script = BoundFiles.ScriptArgument(ancestor.Exe, ancestor.Args);
            if (string.IsNullOrWhiteSpace(script))
                continue;

            string full;
            try
            {
                full = Path.GetFullPath(script);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                continue;
            }

            var blessing = blessings.FirstOrDefault(b =>
                string.Equals(b.Path, full, StringComparison.OrdinalIgnoreCase));
            if (blessing is null)
                continue;

            var hash = hashOf(full);
            if (hash is not null && blessing.Covers(hash, tool, commandClass))
                return true;
        }

        return false;
    }
}

/// <summary>JSON file of reviewed scripts under the product root.</summary>
public sealed class BlessedScriptStore
{
    public const string FileName = "blessed-scripts.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;

    public BlessedScriptStore(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public static string DefaultPath() => Path.Combine(ProductPaths.Root(), FileName);

    public IReadOnlyList<ScriptBlessing> Load()
    {
        if (!File.Exists(_path))
            return [];
        try
        {
            var doc = JsonSerializer.Deserialize<BlessedScriptFile>(File.ReadAllText(_path), JsonOptions);
            return doc?.Scripts?.Where(s => !string.IsNullOrWhiteSpace(s.Path) && !string.IsNullOrWhiteSpace(s.Sha256)).ToList()
                ?? [];
        }
        catch (JsonException)
        {
            // A damaged file must not grant caps.
            return [];
        }
    }

    public void Save(ScriptBlessing blessing)
    {
        var scripts = Load()
            .Where(s => !string.Equals(s.Path, blessing.Path, StringComparison.OrdinalIgnoreCase))
            .Append(blessing)
            .ToList();
        Write(scripts);
    }

    public bool Remove(string path)
    {
        var full = Path.GetFullPath(path);
        var scripts = Load();
        var kept = scripts.Where(s => !string.Equals(s.Path, full, StringComparison.OrdinalIgnoreCase)).ToList();
        if (kept.Count == scripts.Count)
            return false;
        Write(kept);
        return true;
    }

    private void Write(List<ScriptBlessing> scripts)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(new BlessedScriptFile { Scripts = scripts }, JsonOptions);
        File.WriteAllText(_path, json);
    }

    private sealed class BlessedScriptFile
    {
        public List<ScriptBlessing> Scripts { get; set; } = [];
    }
}
