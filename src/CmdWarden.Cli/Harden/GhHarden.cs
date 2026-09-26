using System.Diagnostics;
using System.Text;
using CmdWarden.Contracts;

namespace CmdWarden.Cli.Harden;

public sealed class GhHardenOptions
{
    public string? RealGhPath { get; init; }
    public string? ProductRoot { get; init; }
    public string? ShimSourceDir { get; init; }
    public string? TokenOverride { get; init; }
    public bool SkipTokenImport { get; init; }
    public bool SkipUserPath { get; init; }
    public string? PipeName { get; init; }
}

public sealed class GhHardenResult
{
    public required string RealGhPath { get; init; }
    public required string PinSha256 { get; init; }
    public required string ShimsDir { get; init; }
    public required string ShimExePath { get; init; }
    public bool UserPathUpdated { get; init; }
    public bool TokenImported { get; init; }
    public string? TokenImportNote { get; init; }
}

/// <summary>
/// Compat harden for gh: discover, pin, install shim, import token (issue #33).
/// Does not remove stock gh: keyring entries.
/// </summary>
public static class GhHarden
{
    public const string ToolId = "gh";
    public const string VaultSecretName = "GH_TOKEN";

    public static async Task<GhHardenResult> RunAsync(GhHardenOptions options, CancellationToken cancellationToken = default)
    {
        var root = options.ProductRoot ?? ProductPaths.Root();
        var shimsDir = Path.Combine(root, "shims");
        Directory.CreateDirectory(shimsDir);

        var realGh = options.RealGhPath;
        if (string.IsNullOrWhiteSpace(realGh))
            realGh = GhDiscoverer.FindRealGh(productShimsDir: shimsDir);

        if (string.IsNullOrWhiteSpace(realGh) || !File.Exists(realGh))
            throw new InvalidOperationException(
                "Could not find real gh.exe on PATH (excluding CmdWarden shims). Install GitHub CLI or pass an absolute path.");

        realGh = Path.GetFullPath(realGh);

        // Pin before installing shim so we never pin ourselves.
        var pins = new ToolPinStore(root);
        pins.Save(ToolId, realGh);
        var pin = pins.TryGet(ToolId) ?? throw new InvalidOperationException("Failed to read pin after save.");

        var shimSource = ResolveShimSource(options.ShimSourceDir);
        ShimPayload.Install(shimSource, shimsDir, "gh.exe");

        var shimExe = Path.Combine(shimsDir, "gh.exe");
        if (!File.Exists(shimExe))
            throw new InvalidOperationException($"Shim install incomplete: missing {shimExe}");

        var pathUpdated = false;
        if (!options.SkipUserPath)
            pathUpdated = UserPathEditor.EnsurePrepended(shimsDir);

        var tokenImported = false;
        string? tokenNote = null;
        if (!options.SkipTokenImport)
        {
            byte[]? tokenBytes = null;
            try
            {
                if (!string.IsNullOrEmpty(options.TokenOverride))
                {
                    tokenBytes = Encoding.UTF8.GetBytes(options.TokenOverride);
                }
                else
                {
                    var token = await ReadTokenFromRealGhAsync(realGh, cancellationToken).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(token))
                        throw new InvalidOperationException("gh auth token returned empty output.");
                    tokenBytes = Encoding.UTF8.GetBytes(token.Trim());
                }

                await AgentVaultClient.SaveAsync(VaultSecretName, tokenBytes, options.PipeName)
                    .ConfigureAwait(false);
                tokenImported = true;
                tokenNote = "Imported active token into CmdWarden vault as GH_TOKEN (compat: gh keyring left intact).";
            }
            finally
            {
                if (tokenBytes is not null)
                    Array.Clear(tokenBytes);
            }
        }
        else
        {
            tokenNote = "Token import skipped.";
        }

        return new GhHardenResult
        {
            RealGhPath = realGh,
            PinSha256 = pin.Sha256,
            ShimsDir = shimsDir,
            ShimExePath = shimExe,
            UserPathUpdated = pathUpdated,
            TokenImported = tokenImported,
            TokenImportNote = tokenNote,
        };
    }

    public static string ResolveShimSource(string? overrideDir = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            var d = Path.GetFullPath(overrideDir);
            if (File.Exists(Path.Combine(d, "gh.exe")) || File.Exists(Path.Combine(d, "gh.dll")))
                return d;
            throw new DirectoryNotFoundException("Shim source dir missing gh.exe/gh.dll: " + d);
        }

        var env = Environment.GetEnvironmentVariable("CW_SHIM_SOURCE");
        if (!string.IsNullOrWhiteSpace(env))
        {
            var d = Path.GetFullPath(env);
            if (File.Exists(Path.Combine(d, "gh.exe")) || File.Exists(Path.Combine(d, "gh.dll")))
                return d;
        }

        var payload = Path.Combine(AppContext.BaseDirectory, "shim-payload");
        if (File.Exists(Path.Combine(payload, "gh.exe")) || File.Exists(Path.Combine(payload, "gh.dll")))
            return payload;

        // Dev fallback: walk up for CmdWarden.Shim.Gh bin
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "CmdWarden.Shim.Gh", "bin", "Debug", "net10.0");
            if (File.Exists(Path.Combine(candidate, "gh.exe")) || File.Exists(Path.Combine(candidate, "gh.dll")))
                return candidate;
            var candidateRelease = Path.Combine(dir.FullName, "src", "CmdWarden.Shim.Gh", "bin", "Release", "net10.0");
            if (File.Exists(Path.Combine(candidateRelease, "gh.exe")) || File.Exists(Path.Combine(candidateRelease, "gh.dll")))
                return candidateRelease;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate gh shim payload. Build CmdWarden.Shim.Gh or set CW_SHIM_SOURCE.");
    }


    public static async Task<string> ReadTokenFromRealGhAsync(string realGhPath, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = realGhPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("auth");
        psi.ArgumentList.Add("token");

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start real gh for token import.");
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Real gh auth token failed (exit {process.ExitCode}): {stderr.Trim()}");

        return stdout.Trim();
    }
}
