using CmdWarden.Contracts;

namespace CmdWarden.Cli.Harden;

public sealed class AzHardenOptions
{
    public string? RealAzPath { get; init; }
    public string? ProductRoot { get; init; }
    public string? ShimSourceDir { get; init; }
    public bool SkipUserPath { get; init; }
}

public sealed class AzHardenResult
{
    public required string RealAzPath { get; init; }
    public required string PinSha256 { get; init; }
    public required string ShimsDir { get; init; }
    public required string ShimExePath { get; init; }
    public bool UserPathUpdated { get; init; }
    public string CredentialNote { get; init; } =
        "MSAL / ~/.azure cache left intact (compat mode; no vault migration).";
}

/// <summary>
/// Compat harden for az: discover, pin, install PATH shim (issue #45).
/// Does not strip or migrate MSAL / ~/.azure.
/// </summary>
public static class AzHarden
{
    public const string ToolId = "az";

    public static AzHardenResult Run(AzHardenOptions options)
    {
        var root = options.ProductRoot ?? ProductPaths.Root();
        var shimsDir = Path.Combine(root, "shims");
        Directory.CreateDirectory(shimsDir);

        var realAz = options.RealAzPath;
        if (string.IsNullOrWhiteSpace(realAz))
            realAz = AzDiscoverer.FindRealAz(productShimsDir: shimsDir);

        if (string.IsNullOrWhiteSpace(realAz) || !File.Exists(realAz))
            throw new InvalidOperationException(
                "Could not find real az.cmd/az.exe on PATH (excluding CmdWarden shims). Install Azure CLI or pass an absolute path.");

        realAz = Path.GetFullPath(realAz);

        // Pin before installing shim so we never pin ourselves.
        var pins = new ToolPinStore(root);
        pins.Save(ToolId, realAz);
        var pin = pins.TryGet(ToolId) ?? throw new InvalidOperationException("Failed to read pin after save.");

        var shimSource = ResolveShimSource(options.ShimSourceDir);
        InstallShimPayload(shimSource, shimsDir);

        var shimExe = Path.Combine(shimsDir, "az.exe");
        if (!File.Exists(shimExe))
            throw new InvalidOperationException($"Shim install incomplete: missing {shimExe}");

        var pathUpdated = false;
        if (!options.SkipUserPath)
            pathUpdated = UserPathEditor.EnsurePrepended(shimsDir);

        return new AzHardenResult
        {
            RealAzPath = realAz,
            PinSha256 = pin.Sha256,
            ShimsDir = shimsDir,
            ShimExePath = shimExe,
            UserPathUpdated = pathUpdated,
        };
    }

    public static string ResolveShimSource(string? overrideDir = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            var d = Path.GetFullPath(overrideDir);
            if (File.Exists(Path.Combine(d, "az.exe")) || File.Exists(Path.Combine(d, "az.dll")))
                return d;
            throw new DirectoryNotFoundException("Shim source dir missing az.exe/az.dll: " + d);
        }

        var env = Environment.GetEnvironmentVariable("CW_AZ_SHIM_SOURCE")
            ?? Environment.GetEnvironmentVariable("CW_SHIM_SOURCE");
        if (!string.IsNullOrWhiteSpace(env))
        {
            var d = Path.GetFullPath(env);
            if (File.Exists(Path.Combine(d, "az.exe")) || File.Exists(Path.Combine(d, "az.dll")))
                return d;
        }

        var payload = Path.Combine(AppContext.BaseDirectory, "shim-payload");
        if (File.Exists(Path.Combine(payload, "az.exe")) || File.Exists(Path.Combine(payload, "az.dll")))
            return payload;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "CmdWarden.Shim.Az", "bin", "Debug", "net10.0");
            if (File.Exists(Path.Combine(candidate, "az.exe")) || File.Exists(Path.Combine(candidate, "az.dll")))
                return candidate;
            var candidateRelease = Path.Combine(dir.FullName, "src", "CmdWarden.Shim.Az", "bin", "Release", "net10.0");
            if (File.Exists(Path.Combine(candidateRelease, "az.exe")) || File.Exists(Path.Combine(candidateRelease, "az.dll")))
                return candidateRelease;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate az shim payload. Build CmdWarden.Shim.Az or set CW_AZ_SHIM_SOURCE.");
    }

    public static void InstallShimPayload(string sourceDir, string shimsDir)
    {
        Directory.CreateDirectory(shimsDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var name = Path.GetFileName(file);
            File.Copy(file, Path.Combine(shimsDir, name), overwrite: true);
        }
    }
}
