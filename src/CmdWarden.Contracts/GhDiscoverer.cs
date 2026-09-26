namespace CmdWarden.Contracts;

/// <summary>
/// Locate the real gh.exe for pinning (skip product shims dir).
/// </summary>
public static class GhDiscoverer
{
    public static string? FindRealGh(string? pathEnv = null, string? productShimsDir = null) =>
        ToolDiscoverer.Find(ToolDiscoverer.WindowsNames("gh"), pathEnv, productShimsDir);
}
