namespace CmdWarden.Contracts.Scan;

/// <summary>
/// Coded first-catalog detector (versioned with the product).
/// </summary>
public interface IScanDetector
{
    /// <summary>Stable detector id (e.g. gh.ambient_token).</summary>
    string Id { get; }

    IReadOnlyList<ScanFinding> Detect(ScanContext context);
}
