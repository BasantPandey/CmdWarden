namespace CmdWarden.Contracts;

/// <summary>
/// Decision strings on Authorize / ReleaseSecret responses and audit rows.
/// Shims only read <c>allowed</c>; new values never break older clients.
/// </summary>
public static class GateDecisions
{
    public const string AutoAllow = "auto-allow";
    public const string AllowOnce = "allow-once";
    public const string Deny = "deny";
    public const string Unavailable = "unavailable";
    /// <summary>The call on which the human clicked "Allow for session" (#132).</summary>
    public const string SessionGrant = "session-grant";
    /// <summary>A later call covered by an active session allow (#132).</summary>
    public const string SessionAllow = "session-allow";
}
