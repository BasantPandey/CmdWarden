using System.Security.Cryptography;

namespace CmdWarden.Contracts;

/// <summary>A file an approval covers: the binary or a script, with its SHA-256 at approval time (#30).</summary>
public sealed record BoundFile(string Path, string Sha256);

/// <summary>
/// Time-of-check-to-time-of-use guard (#30). An approval binds the command line, the binary, and
/// each script the command runs. The runner locks those files against writes before it asks, so
/// the content the person approved is the content that runs.
/// </summary>
public static class BoundFiles
{
    private static readonly string[] ScriptExtensions = [".ps1", ".sh", ".bash", ".py", ".js", ".mjs", ".cjs", ".ts", ".rb", ".pl", ".bat", ".cmd"];

    /// <summary>
    /// The files a command runs: the program, and the script its interpreter reads. Only files that
    /// exist count. <paramref name="program"/> is a resolved path or a bare name.
    /// </summary>
    public static IReadOnlyList<string> Find(string program, IReadOnlyList<string> args, string workingDirectory)
    {
        var files = new List<string>();
        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            try
            {
                var full = System.IO.Path.GetFullPath(path, workingDirectory);
                if (File.Exists(full) && !files.Contains(full, StringComparer.OrdinalIgnoreCase))
                    files.Add(full);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
            {
                // Not a path.
            }
        }

        Add(program);
        Add(ScriptArgument(program, args));
        return files;
    }

    /// <summary>The script file an interpreter runs, or null for inline code (-c, -Command, -e) or no script.</summary>
    public static string? ScriptArgument(string program, IReadOnlyList<string> args)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(program).ToLowerInvariant();
        switch (name)
        {
            case "pwsh" or "powershell":
                for (var i = 0; i < args.Count; i++)
                {
                    var a = args[i].ToLowerInvariant();
                    if (a is "-file" or "-f" or "/file" && i + 1 < args.Count)
                        return args[i + 1];
                    if (a is "-command" or "-c" or "-encodedcommand" or "-enc" or "-e" or "-ec")
                        return null;
                    if (!a.StartsWith('-'))
                        return args[i].EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) ? args[i] : null;
                }
                return null;
            case "cmd":
                for (var i = 0; i < args.Count - 1; i++)
                {
                    if (args[i].ToLowerInvariant() is "/c" or "/k")
                        return HasScriptExtension(args[i + 1]) ? args[i + 1] : null;
                }
                return null;
            case "bash" or "sh" or "zsh" or "dash" or "python" or "python3" or "py" or "node" or "deno" or "bun" or "ruby" or "perl":
                // Inline code or a module: no script file.
                string[] inline = name switch
                {
                    "bash" or "sh" or "zsh" or "dash" => ["-c"],
                    "python" or "python3" or "py" => ["-c", "-m"],
                    _ => ["-e", "--eval", "-p", "--print"],
                };
                foreach (var arg in args)
                {
                    if (inline.Contains(arg))
                        return null;
                    if (arg is "run" && name is "deno" or "bun")
                        continue;
                    if (!arg.StartsWith('-'))
                        return arg;
                }
                return null;
            default:
                return null;
        }
    }

    private static bool HasScriptExtension(string path) =>
        ScriptExtensions.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static string Sha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public static IReadOnlyList<BoundFile> Hash(IEnumerable<string> paths) =>
        paths.Select(p => new BoundFile(p, Sha256(p))).ToList();

    /// <summary>One line per file for keys and display: <c>path=sha256</c>, sorted.</summary>
    public static string Key(IEnumerable<BoundFile> files) =>
        string.Join('|', files.Select(f => f.Path.ToLowerInvariant() + "=" + f.Sha256).Order(StringComparer.Ordinal));

    /// <summary>Files whose path is in both lists but whose hash differs: they changed after approval.</summary>
    public static IReadOnlyList<string> Changed(IEnumerable<BoundFile> approved, IEnumerable<BoundFile> now) =>
        now.Where(n => approved.Any(a => string.Equals(a.Path, n.Path, StringComparison.OrdinalIgnoreCase) && a.Sha256 != n.Sha256))
            .Select(n => n.Path)
            .ToList();

    /// <summary>
    /// Holds a read handle on each file that denies writes and deletes. Scripts stay locked until
    /// the child exits; an interpreter reads them as it goes. A locked file another process has
    /// open for write is skipped; the hash check still catches a change.
    /// </summary>
    public static FileLocks Lock(IEnumerable<string> paths)
    {
        var handles = new List<FileStream>();
        foreach (var path in paths)
        {
            try
            {
                handles.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
            }
            catch (IOException)
            {
                // Open for write elsewhere; the hash check is the guard for this file.
            }
        }
        return new FileLocks(handles);
    }

    /// <summary>
    /// Just before the start: the files still hash to what the Agent approved. A mismatch names
    /// the changed files; the runner must not start the child.
    /// </summary>
    public static IReadOnlyList<string> Mismatches(IEnumerable<BoundFile> approved)
    {
        var changed = new List<string>();
        foreach (var file in approved)
        {
            try
            {
                if (Sha256(file.Path) != file.Sha256)
                    changed.Add(file.Path);
            }
            catch (IOException)
            {
                changed.Add(file.Path);
            }
        }
        return changed;
    }

    /// <summary>
    /// A shim just before the start (#30): lock the pinned binary and check it still has the hash
    /// the Agent approved. Null when it changed; the shim must not start it. The lock lasts the run,
    /// so a pinned script such as az.cmd cannot change while cmd reads it.
    /// </summary>
    public static FileLocks? LockVerified(string path, string sha256)
    {
        var locks = Lock([path]);
        if (string.IsNullOrEmpty(sha256) || Mismatches([new BoundFile(path, sha256)]).Count == 0)
            return locks;
        locks.Dispose();
        return null;
    }

    public const string ChangedMessage = "Script changed after approval";
}

public sealed class FileLocks(List<FileStream> handles) : IDisposable
{
    public void Dispose()
    {
        foreach (var h in handles)
            h.Dispose();
        handles.Clear();
    }
}
