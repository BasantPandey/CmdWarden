namespace CmdWarden.Contracts;

/// <summary>
/// Plain stderr for a stopped shim run. The line has no secret and no policy dump.
/// </summary>
public static class ShimStopText
{
    public static string? TryPlain(string tool, string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return null;

        if (detail.Contains(PolicyReasonCodes.UserDenied, StringComparison.Ordinal))
            return Denied(tool);

        if (detail.Contains(PolicyReasonCodes.ApprovalUnavailable, StringComparison.Ordinal))
            return TimedOut(tool);

        return null;
    }

    public static string Denied(string tool) =>
        $"{ProductInfo.Name}: you denied this {tool} command.";

    public static string TimedOut(string tool) =>
        $"{ProductInfo.Name}: the Approval Gate timed out. The {tool} command did not run.";
}
