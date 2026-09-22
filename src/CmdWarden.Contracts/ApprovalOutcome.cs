namespace CmdWarden.Contracts;

/// <summary>
/// Human decision from the Approval Gate (issue #6).
/// </summary>
public enum ApprovalOutcome
{
    /// <summary>
    /// User allowed the release. The decision lasts the launcher session for this one command
    /// class, tool and secret (#205). It never covers secret-reveal and it is no policy change.
    /// </summary>
    AllowOnce = 0,

    /// <summary>User denied the release.</summary>
    Deny = 1,

    /// <summary>UI could not be shown (non-interactive, timeout, error). Fail closed.</summary>
    Unavailable = 2,

    /// <summary>
    /// User granted the launcher process this command class (and lower) for tool + secret
    /// until the launcher exits or goes idle (#132). Never covers secret-reveal.
    /// </summary>
    AllowForSession = 3,
}

/// <summary>
/// Inputs for the Approval Gate (MessageBox transitional + WinUI helper).
/// Never carry secret values — <see cref="SecretName"/> is a name only.
/// </summary>
public sealed record ApprovalRequest(
    string Tool,
    string CommandClass,
    string PolicyLevel,
    string LauncherPolicyKey,
    string LauncherKind,
    string? LauncherPath,
    string SecretName,
    string? Purpose,
    string? EnrollmentKind,
    string? PolicyNote,
    string? CommandLine = null,
    string? ToolPath = null,
    string? WorkingDirectory = null,
    string? LauncherProductName = null,
    string? LauncherFileDescription = null,
    string? LauncherFileName = null,
    string? LauncherPublisher = null,
    DateTimeOffset? RequestedAt = null,
    int? LauncherPid = null)
{
    /// <summary>Secret names only (never values). Defaults to single <see cref="SecretName"/> when set.</summary>
    public IReadOnlyList<string> SecretNames =>
        string.IsNullOrWhiteSpace(SecretName)
            ? Array.Empty<string>()
            : new[] { SecretName };
}

/// <summary>
/// Shared message text for native and headless gates (testable without UI).
/// </summary>
public static class ApprovalPromptText
{
    /// <summary>Window caption for MessageBox transitional UI.</summary>
    public static string Caption => ProductInfo.Name;

    public static string BuildBody(ApprovalRequest request)
    {
        var path = string.IsNullOrWhiteSpace(request.LauncherPath) ? "(unknown)" : request.LauncherPath;
        var note = string.IsNullOrWhiteSpace(request.PolicyNote) ? null : request.PolicyNote;
        var enroll = string.IsNullOrWhiteSpace(request.EnrollmentKind) ? "unknown" : request.EnrollmentKind;
        var display = ApprovalPresentation.ResolveLauncherDisplayName(
            request.LauncherProductName,
            request.LauncherFileDescription,
            request.LauncherFileName,
            request.LauncherPath);
        var reason = ApprovalPresentation.BuildReasonLine(request.Tool, request.SecretName, request.Purpose);

        var lines = new List<string>
        {
            $"{display} {ApprovalPresentation.SubtitleWantsToRun}",
            reason,
            "",
            $"Tool:            {request.Tool}",
            $"Command class:   {request.CommandClass}",
            $"Policy level:    {request.PolicyLevel}",
            $"Enrollment:      {enroll}",
            $"Launcher kind:   {request.LauncherKind}",
            $"Launcher key:    {request.LauncherPolicyKey}",
            $"Launcher path:   {path}",
            $"Secret name:     {request.SecretName}",
        };

        if (!string.IsNullOrWhiteSpace(request.CommandLine))
            lines.Add($"Command:         {request.CommandLine}");
        if (!string.IsNullOrWhiteSpace(request.ToolPath))
            lines.Add($"Tool path:       {request.ToolPath}");
        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
            lines.Add($"cwd:             {request.WorkingDirectory}");

        if (note is not null)
            lines.Add($"Note:            {note}");

        lines.Add("");
        lines.Add("Yes = Approve Once (this command class, until the launcher exits)");
        lines.Add("No  = Deny");
        return string.Join(Environment.NewLine, lines);
    }
}
