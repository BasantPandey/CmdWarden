namespace CmdWarden.Cli.Harden;

/// <summary>
/// Copy shims from a payload folder into the shims dir. The bundled shim-payload folder holds the
/// shim of every tool, so a harden copies only the .exe files it names. Every other file (the
/// .dll and .json files that the apphosts load) is shared and always copied.
/// </summary>
public static class ShimPayload
{
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
    }
}
