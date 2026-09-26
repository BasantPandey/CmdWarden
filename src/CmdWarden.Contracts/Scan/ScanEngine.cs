namespace CmdWarden.Contracts.Scan;

/// <summary>
/// Runs coded detectors for on-demand <c>cw scan</c> (issue #43).
/// Read-only; never auto-hardens.
/// </summary>
public sealed class ScanEngine
{
    private readonly IReadOnlyList<IScanDetector> _detectors;

    public ScanEngine(IEnumerable<IScanDetector>? detectors = null)
    {
        _detectors = (detectors ?? DefaultDetectors()).ToList();
    }

    public IReadOnlyList<IScanDetector> Detectors => _detectors;

    public IReadOnlyList<ScanFinding> Run(ScanContext context)
    {
        var findings = new List<ScanFinding>();
        foreach (var detector in _detectors)
        {
            try
            {
                findings.AddRange(detector.Detect(context));
            }
            catch
            {
                // Best-effort: one failing detector must not abort the scan.
            }
        }

        return findings;
    }

    /// <summary>
    /// Display order for the shell: severity (high, medium, low, info), then First Catalog tool order,
    /// then id. The scan-scope banner is always last (issue #118).
    /// </summary>
    public static IReadOnlyList<ScanFinding> Order(IEnumerable<ScanFinding> findings) =>
        findings
            .OrderBy(f => f.Id == SystemScanBannerDetector.FindingId ? 1 : 0)
            .ThenBy(f => Array.IndexOf(SeverityRank, f.Severity) is var s && s >= 0 ? s : SeverityRank.Length)
            .ThenBy(f => ToolOrder(f.Tool))
            .ThenBy(f => f.Id, StringComparer.Ordinal)
            .ToList();

    private static readonly string[] SeverityRank = [ScanSeverity.High, ScanSeverity.Medium, ScanSeverity.Low, ScanSeverity.Info];

    private static int ToolOrder(string tool)
    {
        for (var i = 0; i < ToolCatalog.Tools.Count; i++)
            if (ToolCatalog.Tools[i].Id == tool)
                return i;
        return ToolCatalog.Tools.Count;
    }

    public static IEnumerable<IScanDetector> DefaultDetectors()
    {
        yield return new GhAmbientTokenDetector();
        yield return new GhAmbientPathDetector();
        yield return new GhNotHardenedDetector();
        yield return new GhAbsolutePathResidualDetector();
        yield return new GitCredentialStoreFileDetector();
        yield return new GitNotHardenedDetector();
        yield return new AzAmbientSpSecretDetector();
        yield return new AzNotHardenedDetector();
        yield return new DockerAmbientAuthConfigDetector();
        yield return new DockerNotHardenedDetector();
        yield return new PackDetector();
        yield return new McpConfigSecretDetector();
        yield return new SystemScanBannerDetector();
    }
}
