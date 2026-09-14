using CmdWarden.Contracts;

namespace CmdWarden.Cli.Harden;

/// <summary>
/// Locate the real git.exe for pinning (skip product shims dir).
/// </summary>
public static class GitDiscoverer
{
    public static string? FindRealGit(string? pathEnv = null, string? productShimsDir = null)
    {
        productShimsDir ??= ProductPaths.ShimsDir();
        var path = pathEnv ?? Environment.GetEnvironmentVariable("PATH") ?? "";
        var entries = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var entry in entries)
        {
            string fullEntry;
            try
            {
                fullEntry = Path.GetFullPath(entry);
            }
            catch
            {
                continue;
            }

            if (IsUnderDirectory(fullEntry, productShimsDir))
                continue;

            // Prefer .exe over cmd wrappers (Git for Windows often has both).
            foreach (var name in new[] { "git.exe", "git.cmd", "git.bat" })
            {
                var candidate = Path.Combine(fullEntry, name);
                if (!File.Exists(candidate))
                    continue;

                try
                {
                    var info = new FileInfo(candidate);
                    if (info.Length == 0)
                        continue;
                }
                catch
                {
                    continue;
                }

                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        try
        {
            var p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var d = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return p.StartsWith(d, StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
