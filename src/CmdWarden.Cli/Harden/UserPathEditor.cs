namespace CmdWarden.Cli.Harden;

/// <summary>
/// Prepend a directory to the per-user PATH (no admin).
/// </summary>
public static class UserPathEditor
{
    /// <summary>
    /// Ensures <paramref name="directory"/> is first on the user PATH.
    /// Returns true if the user PATH was modified.
    /// </summary>
    public static bool EnsurePrepended(string directory)
    {
        var full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
        var parts = current
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p =>
            {
                try { return Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
                catch { return p; }
            })
            .Where(p => !string.Equals(p, full, StringComparison.OrdinalIgnoreCase))
            .ToList();

        parts.Insert(0, full);
        var updated = string.Join(Path.PathSeparator, parts);
        if (string.Equals(current, updated, StringComparison.Ordinal))
            return false;

        Environment.SetEnvironmentVariable("PATH", updated, EnvironmentVariableTarget.User);
        // Also update current process so subsequent discovers in same process see it (optional).
        var processPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process) ?? "";
        if (!processPath.Split(Path.PathSeparator).Any(p =>
                string.Equals(Path.GetFullPath(p.Trim()), full, StringComparison.OrdinalIgnoreCase)))
        {
            Environment.SetEnvironmentVariable("PATH", full + Path.PathSeparator + processPath, EnvironmentVariableTarget.Process);
        }

        return true;
    }

    public static bool IsPrepended(string directory)
    {
        var full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
        var first = current.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p =>
            {
                try { return Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
                catch { return p; }
            })
            .FirstOrDefault();
        return first is not null && string.Equals(first, full, StringComparison.OrdinalIgnoreCase);
    }
}
