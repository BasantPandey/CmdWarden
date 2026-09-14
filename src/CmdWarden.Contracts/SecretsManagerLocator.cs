namespace CmdWarden.Contracts;

/// <summary>
/// Resolves CmdWarden.SecretsManager.exe for Start Menu / doctor / install hooks (ticket #96).
/// Packaged layout: secrets-manager/ next to the CLI tool root (isolated from agent TFMs).
/// </summary>
public static class SecretsManagerLocator
{
    public const string EnvPath = "CW_SECRETS_MANAGER_PATH";
    public const string ExeName = "CmdWarden.SecretsManager.exe";
    public const string BundleFolderName = "secrets-manager";

    /// <param name="baseDirectory">
    /// When set, only probe this root (and secrets-manager/ under it) - no env fallback,
    /// no AppContext or repo walk. Used by tests and precise lookups.
    /// </param>
    public static string? FindExePath(string? baseDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            var fromEnv = Environment.GetEnvironmentVariable(EnvPath);
            if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv.Trim()))
                return Path.GetFullPath(fromEnv.Trim());
        }

        IEnumerable<string> bases;
        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            bases = new[] { baseDirectory };
        }
        else
        {
            bases = new[] { AppContext.BaseDirectory };
        }

        foreach (var b in bases)
        {
            var hit = ProbeBase(b);
            if (hit is not null)
                return hit;
        }

        if (!string.IsNullOrWhiteSpace(baseDirectory))
            return null;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            foreach (var cfg in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(
                    dir.FullName, "src", "CmdWarden.SecretsManager", "bin", cfg, "net10.0-windows", ExeName);
                if (File.Exists(candidate))
                    return candidate;
            }

            if (File.Exists(Path.Combine(dir.FullName, "CmdWarden.sln")))
                break;
            dir = dir.Parent;
        }

        return null;
    }

    private static string? ProbeBase(string b)
    {
        var candidates = new[]
        {
            // Tool / zip root: secrets-manager/CmdWarden.SecretsManager.exe
            Path.Combine(b, BundleFolderName, ExeName),
            // Same folder (dev copy)
            Path.Combine(b, ExeName),
            // Nested under agent/ (alternate layout; still accepted)
            Path.Combine(b, "agent", BundleFolderName, ExeName),
        };

        foreach (var c in candidates)
        {
            try
            {
                var full = Path.GetFullPath(c);
                if (File.Exists(full))
                    return full;
            }
            catch
            {
                // next
            }
        }

        return null;
    }
}
