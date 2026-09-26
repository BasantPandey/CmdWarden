using CmdWarden.Contracts;

namespace CmdWarden.Cli.Harden;

public sealed class GitHardenOptions
{
    public string? RealGitPath { get; init; }
    public string? ProductRoot { get; init; }
    public string? ShimSourceDir { get; init; }
    public string? HelperSourceDir { get; init; }
    public bool SkipUserPath { get; init; }
}

public sealed class GitHardenResult
{
    public required string RealGitPath { get; init; }
    public required string PinSha256 { get; init; }
    public required string ShimsDir { get; init; }
    public required string ShimExePath { get; init; }
    public string? HelperExePath { get; init; }
    public bool UserPathUpdated { get; init; }
    public string CredentialNote { get; init; } =
        "GCM / git credential stores left intact (compat mode; no vault migration).";
}

/// <summary>
/// Compat harden for git: discover, pin, install PATH shim (issue #44).
/// Does not strip or migrate GCM / credential stores.
/// </summary>
public static class GitHarden
{
    public const string ToolId = "git";

    public static GitHardenResult Run(GitHardenOptions options)
    {
        var root = options.ProductRoot ?? ProductPaths.Root();
        var shimsDir = Path.Combine(root, "shims");
        Directory.CreateDirectory(shimsDir);

        var realGit = options.RealGitPath;
        if (string.IsNullOrWhiteSpace(realGit))
            realGit = GitDiscoverer.FindRealGit(productShimsDir: shimsDir);

        if (string.IsNullOrWhiteSpace(realGit) || !File.Exists(realGit))
            throw new InvalidOperationException(
                "Could not find real git.exe on PATH (excluding CmdWarden shims). Install Git for Windows or pass an absolute path.");

        realGit = Path.GetFullPath(realGit);

        // Pin before installing shim so we never pin ourselves.
        var pins = new ToolPinStore(root);
        pins.Save(ToolId, realGit);
        var pin = pins.TryGet(ToolId) ?? throw new InvalidOperationException("Failed to read pin after save.");

        var shimSource = ResolveShimSource(options.ShimSourceDir);
        ShimPayload.Install(shimSource, shimsDir, "git.exe", HelperTools.GitHelperExe);

        var shimExe = Path.Combine(shimsDir, "git.exe");
        if (!File.Exists(shimExe))
            throw new InvalidOperationException($"Shim install incomplete: missing {shimExe}");

        var helperSource = TryResolveHelperSource(options.HelperSourceDir);
        if (helperSource is not null)
            ShimPayload.Install(helperSource, shimsDir, HelperTools.GitHelperExe);
        var helperExe = Path.Combine(shimsDir, HelperTools.GitHelperExe);
        if (!File.Exists(helperExe))
            helperExe = null;

        var pathUpdated = false;
        if (!options.SkipUserPath)
            pathUpdated = UserPathEditor.EnsurePrepended(shimsDir);

        return new GitHardenResult
        {
            RealGitPath = realGit,
            PinSha256 = pin.Sha256,
            ShimsDir = shimsDir,
            ShimExePath = shimExe,
            HelperExePath = helperExe,
            UserPathUpdated = pathUpdated,
        };
    }

    public static string ResolveShimSource(string? overrideDir = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            var d = Path.GetFullPath(overrideDir);
            if (File.Exists(Path.Combine(d, "git.exe")) || File.Exists(Path.Combine(d, "git.dll")))
                return d;
            throw new DirectoryNotFoundException("Shim source dir missing git.exe/git.dll: " + d);
        }

        var env = Environment.GetEnvironmentVariable("CW_GIT_SHIM_SOURCE")
            ?? Environment.GetEnvironmentVariable("CW_SHIM_SOURCE");
        if (!string.IsNullOrWhiteSpace(env))
        {
            var d = Path.GetFullPath(env);
            if (File.Exists(Path.Combine(d, "git.exe")) || File.Exists(Path.Combine(d, "git.dll")))
                return d;
        }

        var payload = Path.Combine(AppContext.BaseDirectory, "shim-payload");
        if (File.Exists(Path.Combine(payload, "git.exe")) || File.Exists(Path.Combine(payload, "git.dll")))
            return payload;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "CmdWarden.Shim.Git", "bin", "Debug", "net10.0");
            if (File.Exists(Path.Combine(candidate, "git.exe")) || File.Exists(Path.Combine(candidate, "git.dll")))
                return candidate;
            var candidateRelease = Path.Combine(dir.FullName, "src", "CmdWarden.Shim.Git", "bin", "Release", "net10.0");
            if (File.Exists(Path.Combine(candidateRelease, "git.exe")) || File.Exists(Path.Combine(candidateRelease, "git.dll")))
                return candidateRelease;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate git shim payload. Build CmdWarden.Shim.Git or set CW_GIT_SHIM_SOURCE.");
    }

    /// <summary>
    /// Directory that holds <c>git-credential-cmdwarden.exe</c>. Null when the helper
    /// is not built yet so compat harden still installs the PATH shim (#206).
    /// </summary>
    public static string? TryResolveHelperSource(string? overrideDir = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            var d = Path.GetFullPath(overrideDir);
            if (File.Exists(Path.Combine(d, HelperTools.GitHelperExe)))
                return d;
            throw new DirectoryNotFoundException(
                "Helper source dir missing " + HelperTools.GitHelperExe + ": " + d);
        }

        var env = Environment.GetEnvironmentVariable("CW_GIT_HELPER_SOURCE");
        if (!string.IsNullOrWhiteSpace(env))
        {
            var d = Path.GetFullPath(env);
            if (File.Exists(Path.Combine(d, HelperTools.GitHelperExe)))
                return d;
        }

        var payload = Path.Combine(AppContext.BaseDirectory, "shim-payload");
        if (File.Exists(Path.Combine(payload, HelperTools.GitHelperExe)))
            return payload;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            foreach (var config in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(
                    dir.FullName, "src", "CmdWarden.Helper.Git", "bin", config, "net10.0");
                if (File.Exists(Path.Combine(candidate, HelperTools.GitHelperExe)))
                    return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

}
