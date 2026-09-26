using CmdWarden.Contracts;

namespace CmdWarden.Cli.Harden;

public sealed record PackHardenResult(ToolPack Pack, string RealPath, string PinSha256, string ShimExePath, string ShimsDir, bool UserPathUpdated);

/// <summary>
/// Harden a tool from its pack (#37): find the real binary, pin it, and install the generic pack
/// shim as &lt;tool&gt;.exe. Compat mode only; the tool's own config stays in place.
/// </summary>
public static class PackHarden
{
    public const string ShimExe = "cw-pack-shim.exe";

    public static PackHardenResult Run(ToolPack pack, string? realPath = null, bool skipUserPath = false,
        string? productRoot = null, string? shimSourceDir = null)
    {
        var root = productRoot ?? ProductPaths.Root();
        var shimsDir = Path.Combine(root, "shims");
        Directory.CreateDirectory(shimsDir);

        var real = string.IsNullOrWhiteSpace(realPath) ? ToolDiscoverer.Find(pack.Binaries, productShimsDir: shimsDir) : realPath;
        if (string.IsNullOrWhiteSpace(real) || !File.Exists(real))
            throw new InvalidOperationException(
                $"Could not find {string.Join(" or ", pack.Binaries)} on PATH (outside the CmdWarden shims). Install {pack.Name} or pass --path.");
        real = Path.GetFullPath(real);

        // Pin before the shim goes in, so the pin is never the shim.
        var pins = new ToolPinStore(root);
        pins.Save(pack.Tool, real);
        var pin = pins.TryGet(pack.Tool) ?? throw new InvalidOperationException("Failed to read pin after save.");

        var source = ResolveShimSource(shimSourceDir);
        ShimPayload.Install(source, shimsDir);
        var shimExe = Path.Combine(shimsDir, pack.Tool + ".exe");
        File.Copy(Path.Combine(source, ShimExe), shimExe, overwrite: true);

        var pathUpdated = !skipUserPath && UserPathEditor.EnsurePrepended(shimsDir);
        return new PackHardenResult(pack, real, pin.Sha256, shimExe, shimsDir, pathUpdated);
    }

    /// <summary>Remove the pin and the &lt;tool&gt;.exe shim. The shared shim files stay for other tools.</summary>
    public static (bool PinRemoved, bool ShimRemoved) Unharden(string tool, string? productRoot = null)
    {
        var root = productRoot ?? ProductPaths.Root();
        var pinRemoved = new ToolPinStore(root).Delete(tool);
        var shim = Path.Combine(root, "shims", tool + ".exe");
        var shimRemoved = File.Exists(shim);
        if (shimRemoved)
            File.Delete(shim);
        return (pinRemoved, shimRemoved);
    }

    public static string ResolveShimSource(string? overrideDir = null)
    {
        var candidates = new List<string?> { overrideDir, Environment.GetEnvironmentVariable("CW_PACK_SHIM_SOURCE"), Path.Combine(AppContext.BaseDirectory, "shim-payload") };
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            foreach (var config in new[] { "Debug", "Release" })
                candidates.Add(Path.Combine(dir.FullName, "src", "CmdWarden.Shim.Pack", "bin", config, "net10.0"));
        }
        return candidates.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d) && File.Exists(Path.Combine(d, ShimExe)))
            ?? throw new InvalidOperationException("Could not locate the pack shim. Build CmdWarden.Shim.Pack or set CW_PACK_SHIM_SOURCE.");
    }
}
