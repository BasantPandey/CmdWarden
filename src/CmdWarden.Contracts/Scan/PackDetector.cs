using System.Text.RegularExpressions;

namespace CmdWarden.Contracts.Scan;

/// <summary>
/// Scan for every tool pack (#37, #38): a plain secret in a config file the pack names, a pack
/// secret set in the environment, and a tool on PATH that is not hardened. Never a value.
/// </summary>
public sealed class PackDetector : IScanDetector
{
    public string Id => "pack";

    public IReadOnlyList<ScanFinding> Detect(ScanContext context)
    {
        var findings = new List<ScanFinding>();
        foreach (var pack in ToolPacks.Load(context.ProductRoot).Packs)
        {
            foreach (var file in pack.SecretFiles)
            {
                if (FirstMatchLine(Resolve(file.Path, context), file.Pattern) is { } hit)
                {
                    findings.Add(new ScanFinding(
                        Id: $"{pack.Tool}.plain_secret_file",
                        Tool: pack.Tool,
                        Severity: ScanSeverity.High,
                        Title: file.Title,
                        Summary: $"A config file of {pack.Name} holds a secret in plain text. Any process of this user, AI harnesses and install scripts too, can read it.",
                        Evidence: $"{hit.Path} line {hit.Line} (value not shown)",
                        Remediation: file.Remediation ?? "Move the secret to the vault with cw save, and remove it from the file.",
                        HardenHint: $"cw harden {pack.Tool}"));
                }
            }

            var set = pack.SecretEnv.Where(context.EnvIsSet).ToList();
            if (set.Count > 0)
            {
                findings.Add(new ScanFinding(
                    Id: $"{pack.Tool}.ambient_secret_env",
                    Tool: pack.Tool,
                    Severity: ScanSeverity.High,
                    Title: $"Ambient {pack.Name} secret in environment",
                    Summary: $"A {pack.Name} secret is set in this process. Harnesses and children inherit it, so the shim cannot gate it.",
                    Evidence: string.Join(", ", set) + " is set (value not shown)",
                    Remediation: $"Save it with cw save <NAME>, unset it in the shell and in profile scripts, or start the AI harness with cw launch <harness>.",
                    HardenHint: $"cw harden {pack.Tool}"));
            }

            if (context.Pins.TryGet(pack.Tool) is null && ToolDiscoverer.Find(pack.Binaries, context.PathEnv, context.ShimsDir) is not null)
            {
                findings.Add(new ScanFinding(
                    Id: $"{pack.Tool}.not_hardened",
                    Tool: pack.Tool,
                    Severity: ScanSeverity.Medium,
                    Title: $"{pack.Tool} found but not hardened",
                    Summary: $"A real {pack.Tool} is on PATH, but CmdWarden has no pin or shim for it. PATH calls are not gated.",
                    Evidence: $"{string.Join(" or ", pack.Binaries)} found outside the product shims dir",
                    Remediation: "Run harden to pin the real binary and install the pack shim.",
                    HardenHint: $"cw harden {pack.Tool}"));
            }
        }
        return findings;
    }

    /// <summary>~ is the user profile. A relative path is in the working folder.</summary>
    private static string Resolve(string path, ScanContext context)
    {
        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
            return Path.Combine(context.UserProfile, path.Length > 2 ? path[2..] : "");
        return Path.IsPathRooted(path) ? path : Path.Combine(context.WorkingDirectory, path);
    }

    private static (string Path, int Line)? FirstMatchLine(string path, string pattern)
    {
        if (!File.Exists(path))
            return null;
        var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
        var number = 0;
        foreach (var line in File.ReadLines(path))
        {
            number++;
            if (regex.IsMatch(line))
                return (path, number);
        }
        return null;
    }
}
