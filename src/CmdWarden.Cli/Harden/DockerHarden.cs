using CmdWarden.Contracts;

namespace CmdWarden.Cli.Harden;

public sealed class DockerHardenOptions
{
    public string? RealDockerPath { get; init; }
    public string? ProductRoot { get; init; }
    public string? ShimSourceDir { get; init; }
    public bool SkipUserPath { get; init; }
}

public sealed class DockerHardenResult
{
    public required string RealDockerPath { get; init; }
    public required string PinSha256 { get; init; }
    public required string ShimsDir { get; init; }
    public required string ShimExePath { get; init; }
    public bool UserPathUpdated { get; init; }
    public string CredentialNote { get; init; } =
        "Desktop / wincred docker store left intact (compat mode; no vault migration). " +
        "Optional child DOCKER_AUTH_CONFIG only when vault secret is configured.";
}

/// <summary>
/// Compat harden for docker: discover, pin, install PATH shim (issue #46) and the
/// docker-credential-cmdwarden helper (#203). Does not strip Desktop/wincred stores.
/// </summary>
public static class DockerHarden
{
    public const string ToolId = "docker";

    public static DockerHardenResult Run(DockerHardenOptions options)
    {
        var root = options.ProductRoot ?? ProductPaths.Root();
        var shimsDir = Path.Combine(root, "shims");
        Directory.CreateDirectory(shimsDir);

        var realDocker = options.RealDockerPath;
        if (string.IsNullOrWhiteSpace(realDocker))
            realDocker = DockerDiscoverer.FindRealDocker(productShimsDir: shimsDir);

        if (string.IsNullOrWhiteSpace(realDocker) || !File.Exists(realDocker))
            throw new InvalidOperationException(
                "Could not find real docker.exe on PATH (excluding CmdWarden shims). Install Docker Desktop/CLI or pass an absolute path.");

        realDocker = Path.GetFullPath(realDocker);

        // Pin before installing shim so we never pin ourselves.
        var pins = new ToolPinStore(root);
        pins.Save(ToolId, realDocker);
        var pin = pins.TryGet(ToolId) ?? throw new InvalidOperationException("Failed to read pin after save.");

        var shimSource = ResolveShimSource(options.ShimSourceDir);
        ShimPayload.Install(shimSource, shimsDir, "docker.exe", HelperTools.DockerHelperExe);
        var helperSource = ResolveHelperSource(options.ShimSourceDir);
        if (!string.Equals(helperSource, shimSource, StringComparison.OrdinalIgnoreCase))
            ShimPayload.Install(helperSource, shimsDir, HelperTools.DockerHelperExe);

        var shimExe = Path.Combine(shimsDir, "docker.exe");
        if (!File.Exists(shimExe))
            throw new InvalidOperationException($"Shim install incomplete: missing {shimExe}");
        var helperExe = Path.Combine(shimsDir, HelperTools.DockerHelperExe);
        if (!File.Exists(helperExe))
            throw new InvalidOperationException($"Shim install incomplete: missing {helperExe}");

        var pathUpdated = false;
        if (!options.SkipUserPath)
            pathUpdated = UserPathEditor.EnsurePrepended(shimsDir);

        return new DockerHardenResult
        {
            RealDockerPath = realDocker,
            PinSha256 = pin.Sha256,
            ShimsDir = shimsDir,
            ShimExePath = shimExe,
            UserPathUpdated = pathUpdated,
        };
    }

    public static string ResolveShimSource(string? overrideDir = null)
    {
        if (string.IsNullOrWhiteSpace(overrideDir))
            return ResolvePayloadSource("docker", "CmdWarden.Shim.Docker");
        var d = Path.GetFullPath(overrideDir);
        if (HasPayload(d, "docker"))
            return d;
        throw new DirectoryNotFoundException("Shim source dir missing docker.exe/docker.dll: " + d);
    }

    /// <summary>
    /// The helper ships next to the shim in shim-payload/. An override dir without the helper
    /// (a dev shim bin/) falls back to the helper's own bin/.
    /// </summary>
    public static string ResolveHelperSource(string? overrideDir = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideDir) && HasPayload(Path.GetFullPath(overrideDir), "docker-credential-cmdwarden"))
            return Path.GetFullPath(overrideDir);
        return ResolvePayloadSource("docker-credential-cmdwarden", "CmdWarden.Helper.Docker");
    }

    private static bool HasPayload(string dir, string baseName) =>
        File.Exists(Path.Combine(dir, baseName + ".exe")) || File.Exists(Path.Combine(dir, baseName + ".dll"));

    private static string ResolvePayloadSource(string baseName, string projectFolder)
    {
        bool Has(string d) => HasPayload(d, baseName);

        var env = Environment.GetEnvironmentVariable("CW_DOCKER_SHIM_SOURCE")
            ?? Environment.GetEnvironmentVariable("CW_SHIM_SOURCE");
        if (!string.IsNullOrWhiteSpace(env) && Has(Path.GetFullPath(env)))
            return Path.GetFullPath(env);

        var payload = Path.Combine(AppContext.BaseDirectory, "shim-payload");
        if (Has(payload))
            return payload;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            foreach (var config in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(dir.FullName, "src", projectFolder, "bin", config, "net10.0");
                if (Has(candidate))
                    return candidate;
            }
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate {baseName} payload. Build {projectFolder} or set CW_DOCKER_SHIM_SOURCE.");
    }

}
