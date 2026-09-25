using System.Text.Json;

namespace CmdWarden.Contracts;

/// <summary>
/// One place that holds canary tokens (#29): a file block that <c>cw canary install</c> added, or a
/// vault entry. The values are fake; no normal work uses them, so a use means an attack.
/// </summary>
/// <param name="Kind"><see cref="CanaryStore.FileKind"/> or <see cref="CanaryStore.VaultKind"/>.</param>
/// <param name="Location">File path, or the vault secret name.</param>
/// <param name="Block">The exact text appended to the file; <c>cw canary remove</c> takes it out again.</param>
public sealed record CanaryEntry(string Kind, string Location, string Block, IReadOnlyList<CanaryToken> Tokens);

public sealed record CanaryToken(string Name, string Value);

/// <summary>The canary list, <c>canaries.json</c> under the product root. The Session Agent reads it on each check.</summary>
public sealed class CanaryStore
{
    public const string FileKind = "file";
    public const string VaultKind = "vault";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public CanaryStore(string? productRoot = null) =>
        Path = System.IO.Path.Combine(productRoot ?? ProductPaths.Root(), "canaries.json");

    public string Path { get; }

    public IReadOnlyList<CanaryEntry> Load()
    {
        try
        {
            return File.Exists(Path)
                ? JsonSerializer.Deserialize<List<CanaryEntry>>(File.ReadAllText(Path), Json) ?? []
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public void Save(IReadOnlyList<CanaryEntry> entries)
    {
        if (entries.Count == 0)
        {
            File.Delete(Path);
            return;
        }
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(entries, Json));
        File.Move(temp, Path, overwrite: true);
    }

    /// <summary>Every canary value as a leak guard entry, named after the token.</summary>
    public IReadOnlyList<KnownSecret> Secrets() =>
        Load().SelectMany(e => e.Tokens).Select(t => new KnownSecret(t.Name, t.Value, IsCanary: true)).ToList();
}
