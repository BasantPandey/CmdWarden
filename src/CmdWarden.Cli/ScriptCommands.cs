using CmdWarden.Contracts;

namespace CmdWarden.Cli;

/// <summary>
/// cw script bless|list|drop (issue 201). The person reviews one file.
/// That exact file may then use the caps. The agent still needs a card.
/// </summary>
public static class ScriptCommands
{
    /// <summary>The review file. A test points this at a temp file.</summary>
    internal static Func<string> StorePath = BlessedScriptStore.DefaultPath;

    private static BlessedScriptStore Open() => new(StorePath());

    public static int Run(string[] args)
    {
        var sub = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        var rest = args.Length > 1 ? args[1..] : [];
        try
        {
            return sub switch
            {
                "bless" => Bless(rest),
                "list" => List(),
                "drop" => Drop(rest),
                _ => Usage(),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"cw script {sub}: {ex.Message}");
            return 1;
        }
    }

    private static int Usage()
    {
        Console.WriteLine("Usage: cw script bless <path> --cap <tool>=<read|write> [--cap ...]");
        Console.WriteLine("       cw script list");
        Console.WriteLine("       cw script drop <path>");
        Console.WriteLine("A run of that exact file may use the caps with no card.");
        Console.WriteLine("The same command from an agent still shows the card.");
        Console.WriteLine("A change of one byte clears the review. A secret reveal still shows the card.");
        return 1;
    }

    private static int Bless(string[] args)
    {
        string? path = null;
        var caps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--cap" && i + 1 < args.Length)
            {
                if (!TryCap(args[++i], out var tool, out var level))
                    return Usage();
                caps[tool] = level;
            }
            else if (args[i].StartsWith('-'))
            {
                return Usage();
            }
            else if (path is null)
            {
                path = args[i];
            }
            else
            {
                return Usage();
            }
        }

        if (path is null || caps.Count == 0)
            return Usage();
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"cw script bless: file not found: {path}");
            return 1;
        }

        var full = Path.GetFullPath(path);
        var blessing = new ScriptBlessing
        {
            Path = full,
            Sha256 = BoundFiles.Sha256(full),
            Caps = caps,
        };
        Open().Save(blessing);
        Ui.Title($"{ProductInfo.Name} script bless");
        Ui.Kv("path", full);
        Ui.Kv("sha256", blessing.Sha256);
        Ui.Kv("caps", string.Join(", ", caps.Select(c => $"{c.Key}={c.Value}")));
        return 0;
    }

    private static int List()
    {
        var scripts = Open().Load();
        if (scripts.Count == 0)
        {
            Console.WriteLine("No reviewed scripts.");
            return 0;
        }

        foreach (var script in scripts)
        {
            var caps = script.Caps is null
                ? ""
                : string.Join(", ", script.Caps.Select(c => $"{c.Key}={c.Value}"));
            Console.WriteLine($"{script.Path}");
            Console.WriteLine($"  {script.Sha256}  {caps}");
        }

        return 0;
    }

    private static int Drop(string[] args)
    {
        if (args.Length != 1)
            return Usage();
        var removed = Open().Remove(args[0]);
        Console.WriteLine(removed ? $"Dropped {Path.GetFullPath(args[0])}" : "No reviewed script at that path.");
        return removed ? 0 : 1;
    }

    private static bool TryCap(string text, out string tool, out string level)
    {
        tool = "";
        level = "";
        var parts = text.Split('=', 2);
        if (parts.Length != 2)
            return false;
        tool = parts[0].Trim().ToLowerInvariant();
        level = parts[1].Trim().ToLowerInvariant();
        if (tool.Length == 0 || tool.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            return false;
        return level is "read" or "write";
    }
}
