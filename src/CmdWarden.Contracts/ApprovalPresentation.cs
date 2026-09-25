using System.Diagnostics;

namespace CmdWarden.Contracts;

/// <summary>
/// Pure presentation helpers for the Approval Gate card (ticket #78).
/// Never includes secret values — names only.
/// </summary>
public static class ApprovalPresentation
{
    public const string UnknownAppDisplayName = "Unknown app";
    public const string SubtitleWantsToRun = "wants to run";

    /// <summary>
    /// ProductName → FileDescription → file name → Unknown app.
    /// Path may supply file name only; full path is never the hero title.
    /// </summary>
    public static string ResolveLauncherDisplayName(
        string? productName,
        string? fileDescription,
        string? fileName,
        string? path)
    {
        if (!string.IsNullOrWhiteSpace(productName))
            return productName.Trim();
        if (!string.IsNullOrWhiteSpace(fileDescription))
            return fileDescription.Trim();
        if (!string.IsNullOrWhiteSpace(fileName))
            return fileName.Trim();
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                var fromPath = Path.GetFileName(path.Trim());
                if (!string.IsNullOrWhiteSpace(fromPath))
                    return fromPath;
            }
            catch
            {
                // ignore invalid path
            }
        }

        return UnknownAppDisplayName;
    }

    public static string BuildReasonLine(string tool, string secretName, string? purpose)
    {
        var t = string.IsNullOrWhiteSpace(tool) ? "tool" : tool.Trim();
        var secret = string.IsNullOrWhiteSpace(secretName) ? "secret" : secretName.Trim();
        if (string.IsNullOrWhiteSpace(purpose))
            return $"{t} needs {secret}";
        return $"{t} needs {secret} for {purpose.Trim()}";
    }

    public static string BuildReasonHeading(string secretName)
    {
        if (string.IsNullOrWhiteSpace(secretName))
            return "Secret requested";
        return $"{secretName.Trim()} requested";
    }

    public static ApprovalHelperPayload ToHelperPayload(ApprovalRequest request)
    {
        var secretNames = request.SecretNames;
        var primarySecret = secretNames.Count > 0 ? secretNames[0] : (request.SecretName ?? "");
        var display = ResolveLauncherDisplayName(
            request.LauncherProductName,
            request.LauncherFileDescription,
            request.LauncherFileName,
            request.LauncherPath);

        return new ApprovalHelperPayload(
            WindowTitle: ProductInfo.Name,
            LauncherDisplayName: display,
            Subtitle: SubtitleWantsToRun,
            CommandLine: request.CommandLine,
            ToolPath: request.ToolPath,
            WorkingDirectory: request.WorkingDirectory,
            SecretNames: secretNames,
            ReasonHeading: request.ChangedFiles is { Count: > 0 }
                ? BoundFiles.ChangedMessage
                : BuildReasonHeading(primarySecret),
            ReasonLine: request.ChangedFiles is { Count: > 0 } changed
                ? "Changed since you approved it: " + string.Join(", ", changed.Select(Path.GetFileName))
                : BuildReasonLine(request.Tool, primarySecret, request.Purpose),
            EnrollmentKind: string.IsNullOrWhiteSpace(request.EnrollmentKind) ? "unknown" : request.EnrollmentKind,
            IdentityKind: string.IsNullOrWhiteSpace(request.LauncherKind) ? "unknown" : request.LauncherKind,
            Publisher: string.IsNullOrWhiteSpace(request.LauncherPublisher)
                ? (string.Equals(request.LauncherKind, LauncherKinds.Authenticode, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : "Unsigned")
                : request.LauncherPublisher,
            LauncherPath: request.LauncherPath,
            PolicyLevel: request.PolicyLevel,
            CommandClass: request.CommandClass,
            PolicyKey: request.LauncherPolicyKey,
            RequestedAt: request.RequestedAt ?? DateTimeOffset.UtcNow,
            Tool: request.Tool,
            PolicyNote: request.PolicyNote,
            SessionAllowOffered: IsSessionAllowOffered(request.EnrollmentKind, request.CommandClass),
            SessionScopeLine: BuildSessionScopeLine(display, request.LauncherPid, request.CommandClass),
            HelloRequired: request.HelloRequired);
    }

    /// <summary>
    /// "Allow for session" is offered only to enrolled launchers and never for secret-reveal (#132).
    /// </summary>
    public static bool IsSessionAllowOffered(string? enrollmentKind, string? commandClass) =>
        enrollmentKind is LauncherEnrollmentKindNames.Terminal or LauncherEnrollmentKindNames.AiHarness
        && !string.Equals(commandClass, CommandClassNames.SecretReveal, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Says how long each answer lasts, e.g. "Both answers last until Claude Code (pid 1234) exits.
    /// Approve Once covers write commands only." Shown only when a session grant is offered (#205).
    /// </summary>
    public static string BuildSessionScopeLine(string launcherDisplayName, int? launcherPid, string? commandClass = null)
    {
        var until = launcherPid is > 0
            ? $"{launcherDisplayName} (pid {launcherPid})"
            : launcherDisplayName;
        var klass = string.IsNullOrWhiteSpace(commandClass) ? "these" : commandClass.Trim();
        return $"Both answers last until {until} exits. Approve Once covers {klass} commands only.";
    }

    /// <summary>
    /// Best-effort PE version resources for launcher display name (Windows).
    /// Safe to call when path missing — returns file name only.
    /// </summary>
    public static (string? ProductName, string? FileDescription, string? FileName) TryReadImageVersionInfo(string? path)
    {
        string? fileName = null;
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                fileName = Path.GetFileName(path);
            }
            catch
            {
                fileName = null;
            }
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return (null, null, fileName);

        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return (
                NullIfWhite(info.ProductName),
                NullIfWhite(info.FileDescription),
                fileName);
        }
        catch
        {
            return (null, null, fileName);
        }
    }

    private static string? NullIfWhite(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>
/// Helper-safe card payload (no secret values). Suitable for JSON to a WinUI helper.
/// </summary>
public sealed record ApprovalHelperPayload(
    string WindowTitle,
    string LauncherDisplayName,
    string Subtitle,
    string? CommandLine,
    string? ToolPath,
    string? WorkingDirectory,
    IReadOnlyList<string> SecretNames,
    string ReasonHeading,
    string ReasonLine,
    string EnrollmentKind,
    string IdentityKind,
    string? Publisher,
    string? LauncherPath,
    string PolicyLevel,
    string CommandClass,
    string PolicyKey,
    DateTimeOffset RequestedAt,
    string Tool,
    string? PolicyNote,
    bool SessionAllowOffered = false,
    string? SessionScopeLine = null,
    bool HelloRequired = false)
{
    // Explicitly no secret value properties — kept for review clarity.
}
