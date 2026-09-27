using System.Text;
using CmdWarden.Contracts;

namespace CmdWarden.Cli;

/// <summary><c>cw env import</c> (#67): move the secret values of a dotenv file into the vault.</summary>
public static class EnvCommands
{
    public static int Run(string[] args, CredentialVault? vault = null, TextReader? input = null)
    {
        if (args is not ["import", ..])
        {
            Console.WriteLine("Usage: cw env import [<file>] [--prefix <P>] [--all] [--yes]");
            Console.WriteLine("  Move secret values of a dotenv file (default .env) into the vault.");
            Console.WriteLine($"  Each moved line becomes KEY={DotEnvFile.RefPrefix}NAME. Comments, empty lines, and plain settings stay.");
            Console.WriteLine("  --prefix <P>  Vault name is P + KEY, for example --prefix myapp_ (default: KEY).");
            Console.WriteLine("  --all         Move every value, not only names such as *_KEY, *_TOKEN, *_SECRET, *PASSWORD*.");
            Console.WriteLine("  --yes         Do not ask before the move.");
            Console.WriteLine("Then run: cw inject --env-file .env -- <command>");
            return args.Length > 0 && args[0] is "-h" or "--help" or "help" ? 0 : 1;
        }

        string? file = null;
        var prefix = "";
        var all = false;
        var yes = false;
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--prefix" when i + 1 < args.Length:
                    prefix = args[++i];
                    break;
                case "--all":
                    all = true;
                    break;
                case "--yes" or "-y":
                    yes = true;
                    break;
                case var a when !a.StartsWith('-') && file is null:
                    file = a;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown env import option: {args[i]}");
                    return 1;
            }
        }
        file ??= ".env";
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"File not found: {file}");
            return 1;
        }

        var text = File.ReadAllText(file);
        var lines = DotEnvFile.Parse(text);
        var moves = lines
            .Where(l => l.Key is not null && l.VaultRef is null && !string.IsNullOrEmpty(l.Value) && (all || DotEnvFile.LooksSecret(l.Key)))
            .Select(l => (Line: l, Vault: prefix + l.Key))
            .ToList();
        if (moves.Count == 0)
        {
            Console.WriteLine($"No secret values to move in {file}.");
            return 0;
        }

        foreach (var (_, name) in moves)
        {
            try
            {
                _ = VaultNames.TargetName(name);
            }
            catch (ArgumentException)
            {
                Console.Error.WriteLine($"{name} is not a valid vault name. Use --prefix with letters, digits, and _.");
                return 1;
            }
        }

        vault ??= new CredentialVault();
        var stored = new HashSet<string>(vault.ListNames(), StringComparer.OrdinalIgnoreCase);
        var conflicts = moves.Where(m => stored.Contains(m.Vault) && !SameValue(vault, m.Vault, m.Line.Value!)).Select(m => m.Vault).ToList();
        if (conflicts.Count > 0)
        {
            Console.Error.WriteLine($"The vault already has a different value for: {string.Join(", ", conflicts)}.");
            Console.Error.WriteLine($"Nothing changed. Use --prefix <project>_ to keep both, or cw delete <NAME> first.");
            return 1;
        }

        Ui.Title($"{ProductInfo.Name} env import {file}");
        foreach (var (line, name) in moves)
            Ui.Kv(line.Key!, $"-> vault {name}{(stored.Contains(name) ? " (same value, already there)" : "")}");
        if (!yes && !Ui.Confirm("Move these values into the vault and rewrite the file?", input))
        {
            Console.WriteLine("Nothing changed.");
            return 1;
        }

        // Vault first: a failed file write leaves the values in both places, never in neither.
        foreach (var (line, name) in moves.Where(m => !stored.Contains(m.Vault)))
            vault.Save(name, Encoding.UTF8.GetBytes(line.Value!));
        var byLine = moves.ToDictionary(m => m.Line, m => m.Vault);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var rewritten = string.Join(newline, lines.Select(l => byLine.TryGetValue(l, out var name) ? DotEnvFile.RefLine(l, name) : l.Raw));
        var temp = file + ".cw-tmp";
        File.WriteAllText(temp, rewritten);
        File.Move(temp, file, overwrite: true);

        Ui.Line($"  {Ui.Ok("Moved.")} {Ui.E($"{moves.Count} value(s) are in the vault; {file} holds no secret value now.")}");
        Ui.Line(Ui.Dim($"  Run: cw inject --env-file {file} -- <command>"));
        return 0;
    }

    private static bool SameValue(CredentialVault vault, string name, string value)
    {
        var bytes = vault.Read(name);
        try
        {
            return Encoding.UTF8.GetString(bytes) == value;
        }
        finally
        {
            Array.Clear(bytes);
        }
    }
}
