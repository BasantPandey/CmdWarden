namespace CmdWarden.Contracts.Scan;

/// <summary>
/// Structured scan finding (issue #43 / spec §12). Never include secret values.
/// </summary>
public sealed record ScanFinding(
    string Id,
    string Tool,
    string Severity,
    string Title,
    string Summary,
    string Evidence,
    string? Remediation = null,
    string? HardenHint = null,
    // A fix the CLI and the Vault can apply, e.g. an MCP secret location (#28). Never a value.
    string? Fix = null);

/// <summary>
/// Severity labels for findings (stable wire/display names).
/// </summary>
public static class ScanSeverity
{
    public const string Info = "info";
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
}
