using System.Text;

namespace CmdWarden.Cli.Hooks;

/// <summary>
/// #66, #70: CmdWarden tables in the Codex <c>config.toml</c>. Each one lives between two marker
/// comments, so an update or a removal touches only that block. A table of the same name that the
/// user wrote is never changed: the add then reports a skip.
/// ponytail: text blocks, not a TOML parser. The block goes at the end of the file, where a new
/// table header is always valid TOML.
/// </summary>
public static class CodexConfig
{
    public enum Change { Added, Updated, Unchanged, UserOwned }

    public static string Path(string? home = null) =>
        System.IO.Path.Combine(home ?? CmdWarden.Contracts.ProductPaths.UserHome(), ".codex", "config.toml");

    private static string Begin(string id) => $"# >>> CmdWarden {id} (managed; cw removes this block)";
    private static string End(string id) => $"# <<< CmdWarden {id}";

    /// <summary>Adds or updates the block. <paramref name="table"/> is the first table name in it, for the ownership check.</summary>
    public static Change Set(string path, string id, string table, string body)
    {
        var text = File.Exists(path) ? File.ReadAllText(path) : "";
        var (outside, current) = Split(text, id);
        if (HasTable(outside, table))
            return Change.UserOwned;
        var block = $"{Begin(id)}\n{body.TrimEnd()}\n{End(id)}\n";
        if (current == block)
            return Change.Unchanged;
        var trimmed = outside.TrimEnd();
        Write(path, (trimmed.Length == 0 ? "" : trimmed + "\n\n") + block);
        return current is null ? Change.Added : Change.Updated;
    }

    public static bool Remove(string path, string id)
    {
        if (!File.Exists(path))
            return false;
        var (outside, current) = Split(File.ReadAllText(path), id);
        if (current is null)
            return false;
        Write(path, outside.TrimEnd() + "\n");
        return true;
    }

    public static bool Has(string path, string id) =>
        File.Exists(path) && Split(File.ReadAllText(path), id).Block is not null;

    /// <summary>A TOML literal string: no escapes, so a Windows path stays as it is.</summary>
    public static string Literal(string value) =>
        value.Contains('\'') || value.Contains('\n')
            ? "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\""
            : $"'{value}'";

    private static (string Outside, string? Block) Split(string text, string id)
    {
        text = text.Replace("\r\n", "\n");
        var begin = text.IndexOf(Begin(id), StringComparison.Ordinal);
        if (begin < 0)
            return (text, null);
        var endMarker = End(id);
        var end = text.IndexOf(endMarker, begin, StringComparison.Ordinal);
        if (end < 0)
            return (text, null);
        end += endMarker.Length;
        if (end < text.Length && text[end] == '\n')
            end++;
        return (text[..begin] + text[end..], text[begin..end]);
    }

    /// <summary>True when a header <c>[table]</c> or <c>[table.x]</c> is in the text.</summary>
    private static bool HasTable(string text, string table) =>
        text.Split('\n').Select(l => l.Trim()).Any(l =>
            l.StartsWith('[') && !l.StartsWith("[[", StringComparison.Ordinal)
            && (l.StartsWith($"[{table}]", StringComparison.Ordinal) || l.StartsWith($"[{table}.", StringComparison.Ordinal)));

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var temp = path + ".cw-tmp";
        File.WriteAllText(temp, text, new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }
}
