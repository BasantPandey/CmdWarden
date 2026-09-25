namespace CmdWarden.Contracts;

/// <summary>
/// Stable reason codes for policy denials (gRPC messages / audit).
/// </summary>
public static class PolicyReasonCodes
{
    public const string PolicyDeny = "PolicyDeny";
    public const string UnknownLauncher = "UnknownLauncher";
    public const string NeedsApproval = "NeedsApproval";
    public const string NotEnrolled = "NotEnrolled";
    public const string UserDenied = "UserDenied";
    public const string ApprovalUnavailable = "ApprovalUnavailable";
    public const string PinMissing = "PinMissing";
    public const string PinMismatch = "PinMismatch";
    public const string AgentDown = "AgentDown";
    /// <summary>Human decision reused for an exact retry from the same process (#131).</summary>
    public const string TransientReuse = "TransientReuse";
    /// <summary>Session allow granted earlier to this launcher process covered the call (#132).</summary>
    public const string SessionAllow = "SessionAllow";
    /// <summary>A recent human deny for this launcher process and tool stopped a new prompt.</summary>
    public const string DenyCooldown = "DenyCooldown";
    /// <summary>Credential helper chain has no pinned real tool directly above it (#202).</summary>
    public const string HelperParentMissing = "HelperParentMissing";
    /// <summary>A granted shim run is still alive in the helper's chain (#202).</summary>
    public const string RunCovered = "RunCovered";
    /// <summary>Git helper store of an identical value, or erase of a mismatch (#205).</summary>
    public const string Unchanged = "Unchanged";
    /// <summary>Windows Hello confirmed the person after Approve (#24).</summary>
    public const string HelloVerified = "HelloVerified";
    /// <summary>Hello was required but is not set up on this PC; the plain popup decided (#24).</summary>
    public const string HelloUnavailable = "HelloUnavailable";
    /// <summary>The person cancelled Windows Hello, so the popup denied (#24).</summary>
    public const string HelloCanceled = "HelloCanceled";
}
