using System.Diagnostics;

namespace CmdWarden.Contracts;

/// <summary>
/// Global git config reads and writes through the pinned <c>git.exe</c> (#207).
/// <paramref name="GlobalPath"/> pins <c>GIT_CONFIG_GLOBAL</c> for tests; null clears it so the
/// real default global file is used, the same file git reads under the strong-mode shim.
/// </summary>
public sealed record GitGlobalConfig(string GitExe, string? GlobalPath = null)
{
    public const string HelperKey = "credential.helper";

    /// <summary>All values of a multi-valued key, in file order. Empty when unset.</summary>
    public IReadOnlyList<string> GetAll(string key, bool global = true)
    {
        var (exit, stdout) = Run(global ? ["config", "--global", "-z", "--get-all", key] : ["config", "-z", "--get-all", key]);
        return exit == 0 ? SplitNul(stdout) : Array.Empty<string>();
    }

    /// <summary>One value from any scope, or null.</summary>
    public string? Get(string key)
    {
        var (exit, stdout) = Run(["config", "--get", key]);
        return exit == 0 ? stdout.Trim() : null;
    }

    /// <summary>Every global <c>credential.&lt;url&gt;.helper</c> line as key and value, in file order.</summary>
    public IReadOnlyList<ConfigLine> GetScopedHelpers()
    {
        var (exit, stdout) = Run(["config", "--global", "-z", "--get-regexp", @"^credential\..+\.helper$"]);
        if (exit != 0)
            return Array.Empty<ConfigLine>();
        var lines = new List<ConfigLine>();
        // -z form: key, newline, value, NUL. An empty value has no newline.
        foreach (var entry in SplitNul(stdout))
        {
            var newline = entry.IndexOf('\n');
            lines.Add(newline < 0
                ? new ConfigLine { Key = entry, Value = "" }
                : new ConfigLine { Key = entry[..newline], Value = entry[(newline + 1)..] });
        }
        return lines;
    }

    public void ReplaceAll(string key, string value) => RunOrThrow(["config", "--global", "--replace-all", key, value]);

    public void Add(string key, string value) => RunOrThrow(["config", "--global", "--add", key, value]);

    /// <summary>Unset every value; a key that is already absent is not an error.</summary>
    public void UnsetAll(string key)
    {
        var (exit, _) = Run(["config", "--global", "--unset-all", key]);
        if (exit is not (0 or 5))
            throw new InvalidOperationException($"git config --unset-all {key} failed (exit {exit}).");
    }

    /// <summary>Replace every value of <paramref name="key"/> with <paramref name="values"/> in order.</summary>
    public void Set(string key, IReadOnlyList<string> values)
    {
        UnsetAll(key);
        foreach (var value in values)
            Add(key, value);
    }

    /// <summary>
    /// The helper path as gh and GCM write it for <c>sh -c</c>: forward slashes, space and parens escaped.
    /// </summary>
    public static string ShPath(string path)
    {
        var s = path.Replace('\\', '/');
        return s.Replace(" ", @"\ ").Replace("(", @"\(").Replace(")", @"\)");
    }

    /// <summary>gh's IsOurs rule: <c>!</c> then a program named gh followed by <c>auth git-credential</c>.</summary>
    public static bool IsGhHelperValue(string value)
    {
        var v = value.Trim();
        if (!v.StartsWith('!'))
            return false;
        v = v[1..].Trim();
        var auth = v.LastIndexOf(" auth git-credential", StringComparison.Ordinal);
        if (auth < 0 || auth + " auth git-credential".Length != v.Length)
            return false;
        var program = v[..auth].Trim().Trim('\'', '"');
        var name = program.Replace('\\', '/');
        var slash = name.LastIndexOf('/');
        if (slash >= 0)
            name = name[(slash + 1)..];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return name.Equals("gh", StringComparison.OrdinalIgnoreCase);
    }

    private void RunOrThrow(string[] args)
    {
        var (exit, stdout) = Run(args);
        if (exit != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed (exit {exit}): {stdout.Trim()}");
    }

    private (int Exit, string Stdout) Run(string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = GitExe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        if (GlobalPath is null)
            psi.Environment.Remove("GIT_CONFIG_GLOBAL");
        else
            psi.Environment["GIT_CONFIG_GLOBAL"] = GlobalPath;

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, process.ExitCode == 0 ? stdout : stdout + stderr);
    }

    /// <summary>NUL-terminated entries; an empty value stays as an empty entry.</summary>
    private static List<string> SplitNul(string text)
    {
        var parts = text.Split('\0').ToList();
        if (parts.Count > 0 && parts[^1].Length == 0)
            parts.RemoveAt(parts.Count - 1);
        return parts;
    }
}
