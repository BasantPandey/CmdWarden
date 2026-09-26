using CmdWarden.Contracts;

namespace CmdWarden.Cli.Harden;

/// <summary>
/// Locate the real docker.exe for pinning (skip product shims dir).
/// </summary>
public static class DockerDiscoverer
{
    public static string? FindRealDocker(string? pathEnv = null, string? productShimsDir = null) =>
        ToolDiscoverer.Find(ToolDiscoverer.WindowsNames("docker"), pathEnv, productShimsDir);
}
