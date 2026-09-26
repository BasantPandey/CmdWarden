namespace CmdWarden.Contracts;

/// <summary>
/// Locate the real binary of a tool for pinning. The product shims dir is skipped, so a harden
/// never pins a shim.
/// </summary>
public static class ToolDiscoverer
{
    /// <param name="names">File names to try in each PATH entry, in order, for example git.exe, git.cmd.</param>
    public static string? Find(IReadOnlyList<string> names, string? pathEnv = null, string? productShimsDir = null)
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

            foreach (var name in names)
            {
                var candidate = Path.Combine(fullEntry, name);
                // Skip zero-length App Execution Alias stubs.
                try
                {
                    if (!File.Exists(candidate) || new FileInfo(candidate).Length == 0)
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

    /// <summary>name.exe, name.cmd, name.bat: the usual order for a tool on Windows.</summary>
    public static string[] WindowsNames(string tool) => [tool + ".exe", tool + ".cmd", tool + ".bat"];

    private static bool IsUnderDirectory(string path, string directory)
    {
        try
        {
            var p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var d = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(p, d, StringComparison.OrdinalIgnoreCase)
                || (p + Path.DirectorySeparatorChar).StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
