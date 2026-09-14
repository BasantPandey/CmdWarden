namespace CmdWarden.Contracts.Scan;

/// <summary>GH_TOKEN / GITHUB_TOKEN ambient in process environment.</summary>
public sealed class GhAmbientTokenDetector : IScanDetector
{
    public string Id => "gh.ambient_token";

    public IReadOnlyList<ScanFinding> Detect(ScanContext context)
    {
        var set = new List<string>();
        if (context.EnvIsSet("GH_TOKEN"))
            set.Add("GH_TOKEN");
        if (context.EnvIsSet("GITHUB_TOKEN"))
            set.Add("GITHUB_TOKEN");
        if (set.Count == 0)
            return Array.Empty<ScanFinding>();

        return new[]
        {
            new ScanFinding(
                Id: Id,
                Tool: "gh",
                Severity: ScanSeverity.High,
                Title: "Ambient GitHub token in environment",
                Summary:
                "A GitHub token env var is set in this process. Harnesses and children inherit it, bypassing CmdWarden vault release.",
                Evidence: string.Join(", ", set) + " is set (value not shown)",
                Remediation:
                "Unset GH_TOKEN / GITHUB_TOKEN from the current shell and remove exports from profile scripts.",
                HardenHint: "cw harden gh"),
        };
    }
}

/// <summary>GH_PATH ambient can point tools at a real binary and skip the PATH shim.</summary>
public sealed class GhAmbientPathDetector : IScanDetector
{
    public string Id => "gh.ambient_path";

    public IReadOnlyList<ScanFinding> Detect(ScanContext context)
    {
        if (!context.EnvIsSet("GH_PATH"))
            return Array.Empty<ScanFinding>();

        // Evidence: presence and whether path exists - never treat path as secret.
        var raw = context.GetEnv("GH_PATH") ?? "";
        var exists = false;
        try { exists = File.Exists(raw); } catch { /* ignore */ }

        return new[]
        {
            new ScanFinding(
                Id: Id,
                Tool: "gh",
                Severity: ScanSeverity.Medium,
                Title: "Ambient GH_PATH set",
                Summary:
                "GH_PATH is set. Nested go-gh / helpers may invoke that binary and skip the CmdWarden PATH shim.",
                Evidence: exists
                    ? "GH_PATH points at an existing path (value not expanded as secret)"
                    : "GH_PATH is set (path missing or inaccessible)",
                Remediation: "Unset GH_PATH unless you intentionally pin a real gh for non-shim workflows.",
                HardenHint: "cw harden gh"),
        };
    }
}

/// <summary>gh present on PATH but no CmdWarden pin.</summary>
public sealed class GhNotHardenedDetector : IScanDetector
{
    public string Id => "gh.not_hardened";

    public IReadOnlyList<ScanFinding> Detect(ScanContext context)
    {
        if (context.Pins.TryGet("gh") is not null)
            return Array.Empty<ScanFinding>();

        var real = GhDiscoverer.FindRealGh(context.PathEnv, context.ShimsDir);
        if (real is null)
            return Array.Empty<ScanFinding>();

        return new[]
        {
            new ScanFinding(
                Id: Id,
                Tool: "gh",
                Severity: ScanSeverity.Medium,
                Title: "gh found but not hardened",
                Summary:
                "A real gh binary is on PATH, but CmdWarden has no pin/shim for gh. PATH invocations are unmediated.",
                Evidence: "real gh discovered outside product shims dir (path recorded in pin only after harden)",
                Remediation: "Run harden to pin the real binary and install the PATH shim.",
                HardenHint: "cw harden gh"),
        };
    }
}

/// <summary>
/// Residual: absolute path to real gh bypasses PATH harden even after pin (compat mode).
/// </summary>
public sealed class GhAbsolutePathResidualDetector : IScanDetector
{
    public string Id => "gh.absolute_path_residual";

    public IReadOnlyList<ScanFinding> Detect(ScanContext context)
    {
        var pin = context.Pins.TryGet("gh");
        if (pin is null)
            return Array.Empty<ScanFinding>();

        // Only raise residual when harden already happened (pin exists).
        return new[]
        {
            new ScanFinding(
                Id: Id,
                Tool: "gh",
                Severity: ScanSeverity.Info,
                Title: "Absolute-path bypass residual for gh",
                Summary:
                "Compat harden mediates PATH `gh` only. Callers that invoke the real binary by absolute path still bypass the shim.",
                Evidence: "pin present for gh; residual applies to absolute-path launches of the real binary",
                Remediation:
                "Prefer PATH-mediated gh for harnesses; optional strong mode later can remove ambient keyring tokens.",
                HardenHint: "cw harden gh"),
        };
    }
}

/// <summary>Plaintext ~/.git-credentials (store helper residue).</summary>
public sealed class GitCredentialStoreFileDetector : IScanDetector
{
    public string Id => "git.credential_store_file";

    public IReadOnlyList<ScanFinding> Detect(ScanContext context)
    {
        if (string.IsNullOrWhiteSpace(context.UserProfile))
            return Array.Empty<ScanFinding>();

        var path = Path.Combine(context.UserProfile, ".git-credentials");
        if (!File.Exists(path))
            return Array.Empty<ScanFinding>();

        return new[]
        {
            new ScanFinding(
                Id: Id,
                Tool: "git",
                Severity: ScanSeverity.High,
                Title: "Plaintext git credential store file",
                Summary:
                "A ~/.git-credentials file exists. credential.helper=store keeps passwords in plaintext on disk.",
                Evidence: ".git-credentials exists under user profile (contents not read)",
                Remediation:
                "Migrate to GCM/wincredman or product-mediated auth; remove or empty the store file after migration.",
                HardenHint: "cw harden git"),
        };
    }
}

/// <summary>git on PATH without pin (best-effort discovery).</summary>
public sealed class GitNotHardenedDetector : IScanDetector
{
    public string Id => "git.not_hardened";

    public IReadOnlyList<ScanFinding> Detect(ScanContext context)
    {
        if (context.Pins.TryGet("git") is not null)
            return Array.Empty<ScanFinding>();

        var real = FindToolOnPath(context, "git");
        if (real is null)
            return Array.Empty<ScanFinding>();

        return new[]
        {
            new ScanFinding(
                Id: Id,
                Tool: "git",
                Severity: ScanSeverity.Low,
                Title: "git found but not hardened",
                Summary:
                "git is on PATH without a CmdWarden pin. Network/credential ops are unmediated by the product gate.",
                Evidence: "git.exe/cmd found on PATH outside product shims",
                Remediation: "When git harden is available, pin and shim PATH git.",
                HardenHint: "cw harden git"),
        };
    }

    internal static string? FindToolOnPath(ScanContext context, string toolBase)
    {
        var shims = context.ShimsDir;
        foreach (var entry in context.PathEnv.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string fullEntry;
            try { fullEntry = Path.GetFullPath(entry); }
            catch { continue; }

            if (IsUnder(fullEntry, shims))
                continue;

            foreach (var name in new[] { toolBase + ".exe", toolBase + ".cmd", toolBase + ".bat", toolBase })
            {
                var candidate = Path.Combine(fullEntry, name);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static bool IsUnder(string path, string directory)
    {
        try
        {
            var p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var d = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return p.StartsWith(d, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>AZURE_CLIENT_SECRET (and related SP env) ambient.</summary>
public sealed class AzAmbientSpSecretDetector : IScanDetector
{
    public string Id => "az.ambient_sp_secret";

    public IReadOnlyList<ScanFinding> Detect(ScanContext context)
    {
        var names = new[]
        {
            "AZURE_CLIENT_SECRET",
            "AZURE_CLIENT_CERTIFICATE_PATH",
            "AZURE_FEDERATED_TOKEN_FILE",
        };
        var set = names.Where(context.EnvIsSet).ToList();
        if (set.Count == 0)
            return Array.Empty<ScanFinding>();

        return new[]
        {
            new ScanFinding(
                Id: Id,
                Tool: "az",
                Severity: ScanSeverity.High,
                Title: "Ambient Azure service-principal credentials in environment",
                Summary:
                "Service principal material is present in the process environment and is inherited by children.",
                Evidence: string.Join(", ", set) + " is set (values not shown)",
                Remediation: "Unset SP env vars from shells and harness configs; use product-mediated release when available.",
                HardenHint: "cw harden az"),
        };
    }
}

public sealed class AzNotHardenedDetector : IScanDetector
{
    public string Id => "az.not_hardened";

    public IReadOnlyList<ScanFinding> Detect(ScanContext context)
    {
        if (context.Pins.TryGet("az") is not null)
            return Array.Empty<ScanFinding>();

        var real = GitNotHardenedDetector.FindToolOnPath(context, "az");
        if (real is null)
            return Array.Empty<ScanFinding>();

        return new[]
        {
            new ScanFinding(
                Id: Id,
                Tool: "az",
                Severity: ScanSeverity.Low,
                Title: "az found but not hardened",
                Summary: "Azure CLI is on PATH without a CmdWarden pin/shim.",
                Evidence: "az found on PATH outside product shims",
                Remediation: "When az harden is available, pin and shim PATH az.",
                HardenHint: "cw harden az"),
        };
    }
}

/// <summary>DOCKER_AUTH_CONFIG ambient (base64 auths in env).</summary>
public sealed class DockerAmbientAuthConfigDetector : IScanDetector
{
    public string Id => "docker.ambient_auth_config";

    public IReadOnlyList<ScanFinding> Detect(ScanContext context)
    {
        if (!context.EnvIsSet("DOCKER_AUTH_CONFIG"))
            return Array.Empty<ScanFinding>();

        return new[]
        {
            new ScanFinding(
                Id: Id,
                Tool: "docker",
                Severity: ScanSeverity.High,
                Title: "Ambient DOCKER_AUTH_CONFIG in environment",
                Summary:
                "DOCKER_AUTH_CONFIG holds registry credentials in process env and overrides the Docker config store for matching registries.",
                Evidence: "DOCKER_AUTH_CONFIG is set (JSON/value not shown)",
                Remediation:
                "Unset DOCKER_AUTH_CONFIG from shells and CI harnesses; prefer vault-mediated child inject after harden.",
                HardenHint: "cw harden docker"),
        };
    }
}

public sealed class DockerNotHardenedDetector : IScanDetector
{
    public string Id => "docker.not_hardened";

    public IReadOnlyList<ScanFinding> Detect(ScanContext context)
    {
        if (context.Pins.TryGet("docker") is not null)
            return Array.Empty<ScanFinding>();

        var real = GitNotHardenedDetector.FindToolOnPath(context, "docker");
        if (real is null)
            return Array.Empty<ScanFinding>();

        return new[]
        {
            new ScanFinding(
                Id: Id,
                Tool: "docker",
                Severity: ScanSeverity.Low,
                Title: "docker found but not hardened",
                Summary: "docker CLI is on PATH without a CmdWarden pin/shim.",
                Evidence: "docker found on PATH outside product shims",
                Remediation: "When docker harden is available, pin and shim PATH docker.",
                HardenHint: "cw harden docker"),
        };
    }
}

/// <summary>Light system note so empty machines still show scan ran.</summary>
public sealed class SystemScanBannerDetector : IScanDetector
{
    public const string FindingId = "system.scan_scope";

    public string Id => FindingId;

    public IReadOnlyList<ScanFinding> Detect(ScanContext context)
    {
        return new[]
        {
            new ScanFinding(
                Id: Id,
                Tool: "system",
                Severity: ScanSeverity.Info,
                Title: "First-catalog scan scope",
                Summary:
                "Scan covers coded detectors for gh/git/az/docker residual risks plus light system notes. No FS watcher; no auto-harden.",
                Evidence: "product root: " + context.ProductRoot,
                Remediation: "Re-run cw scan after harden or env changes.",
                HardenHint: null),
        };
    }
}
