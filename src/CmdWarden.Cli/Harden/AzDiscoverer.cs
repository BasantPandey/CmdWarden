using CmdWarden.Contracts;

namespace CmdWarden.Cli.Harden;

/// <summary>
/// Locate the real Azure CLI entrypoint for pinning (skip product shims dir).
/// Prefer az.cmd (common winget/MSI layout), then az.exe.
/// </summary>
public static class AzDiscoverer
{
    public static string? FindRealAz(string? pathEnv = null, string? productShimsDir = null)
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

            // az.cmd is the usual user-facing entry; az.exe exists on some layouts.
            foreach (var name in new[] { "az.cmd", "az.exe", "az.bat" })
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
