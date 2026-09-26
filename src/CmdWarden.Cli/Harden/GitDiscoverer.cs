using CmdWarden.Contracts;

namespace CmdWarden.Cli.Harden;

/// <summary>
/// Locate the real git.exe for pinning (skip product shims dir).
/// </summary>
public static class GitDiscoverer
{
    public static string? FindRealGit(string? pathEnv = null, string? productShimsDir = null) =>
        ToolDiscoverer.Find(ToolDiscoverer.WindowsNames("git"), pathEnv, productShimsDir);
}
