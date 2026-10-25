namespace CmdWarden.Contracts;

/// <summary>
/// Absolute path of a program, the way a shell finds it. A path as given stays. Else the first PATH
/// entry with the name, or the name plus a PATHEXT extension. The name itself when nothing matches.
/// A start uses this path, so the binary is not chosen by a later PATH search.
/// </summary>
public static class ProgramPath
{
    public static string Resolve(string fileName, string? pathEnv = null, string? pathExt = null)
    {
        if (fileName.Contains('\\') || fileName.Contains('/') || Path.IsPathRooted(fileName))
            return File.Exists(fileName) ? Path.GetFullPath(fileName) : fileName;
        var extensions = (pathExt ?? Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var names = Path.HasExtension(fileName) ? new[] { fileName } : extensions.Select(e => fileName + e).ToArray();
        foreach (var dir in (pathEnv ?? Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in names)
            {
                try
                {
                    var candidate = Path.Combine(dir, name);
                    if (File.Exists(candidate))
                        // The name as stored on disk, not the PATHEXT case (claude.exe, not claude.EXE).
                        return Directory.EnumerateFiles(Path.GetFullPath(dir), name).FirstOrDefault() ?? Path.GetFullPath(candidate);
                }
                catch (ArgumentException)
                {
                    // Bad PATH entry.
                }
            }
        }
        return fileName;
    }
}
