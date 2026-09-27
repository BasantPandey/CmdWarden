namespace CmdWarden.Cli.Harden;

/// <summary>
/// Copy shims from a payload folder into the shims dir. The bundled shim-payload folder holds the
/// shim of every tool, so a harden copies only the .exe files it names. Every other file (the
/// .dll and .json files that the apphosts load) is shared and always copied.
/// </summary>
public static class ShimPayload
{
    /// <summary>The folder with the private .NET runtime, next to shim-payload and next to shims (#64).</summary>
    public const string RuntimeFolder = "runtime";

    /// <summary>
    /// The folders that hold one folder per version. A shim only needs the base framework; the
    /// ASP.NET and WPF frameworks stay behind.
    /// </summary>
    private static readonly string[] ShimRuntimeParts = [Path.Combine("host", "fxr"), Path.Combine("shared", "Microsoft.NETCore.App")];

    public static void Install(string sourceDir, string shimsDir, params string[] exes)
    {
        Directory.CreateDirectory(shimsDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !exes.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;
            File.Copy(file, Path.Combine(shimsDir, name), overwrite: true);
        }
        CopyRuntime(sourceDir, shimsDir);
    }

    /// <summary>
    /// #64: a portable build has runtime\ next to shim-payload\, and its shims look for ..\runtime.
    /// The shims run from the shims dir, so the base runtime goes next to it too. A version folder
    /// never changes, so one that is there already stays.
    /// </summary>
    public static void CopyRuntime(string sourceDir, string shimsDir)
    {
        var source = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(sourceDir).TrimEnd('\\'))!, RuntimeFolder);
        var target = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(shimsDir).TrimEnd('\\'))!, RuntimeFolder);
        if (!Directory.Exists(Path.Combine(source, "host")) || string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            return;
        foreach (var part in ShimRuntimeParts)
        {
            var from = Path.Combine(source, part);
            if (!Directory.Exists(from))
                continue;
            foreach (var version in Directory.GetDirectories(from))
            {
                var to = Path.Combine(target, part, Path.GetFileName(version));
                if (!Directory.Exists(to))
                    CopyTree(version, to);
            }
        }
    }

    private static void CopyTree(string from, string to)
    {
        var temp = to + ".cw-tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        if (Directory.Exists(temp))
            Directory.Delete(temp, recursive: true);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(temp, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest);
        }
        // A half copy never looks like a full version folder.
        Directory.Move(temp, to);
    }
}
