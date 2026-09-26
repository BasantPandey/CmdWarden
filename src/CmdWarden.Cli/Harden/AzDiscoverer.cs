using CmdWarden.Contracts;

namespace CmdWarden.Cli.Harden;

/// <summary>
/// Locate the real Azure CLI entrypoint for pinning (skip product shims dir).
/// Prefer az.cmd (common winget/MSI layout), then az.exe.
/// </summary>
public static class AzDiscoverer
{
    public static string? FindRealAz(string? pathEnv = null, string? productShimsDir = null) =>
        ToolDiscoverer.Find(["az.cmd", "az.exe", "az.bat"], pathEnv, productShimsDir);
}
