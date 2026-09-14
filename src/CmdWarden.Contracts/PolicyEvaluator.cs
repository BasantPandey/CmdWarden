namespace CmdWarden.Contracts;

/// <summary>
/// Pure policy evaluation: Policy Level × Command Class (CONTEXT.md / issue #5).
/// </summary>
public static class PolicyEvaluator
{
    /// <summary>
    /// Whether the level auto-allows the command class without an Approval Gate.
    /// </summary>
    public static bool IsAutoAllowed(PolicyLevel level, CommandClass commandClass)
    {
        // Full auto-allows every class, including unknown and secret-reveal.
        if (level == PolicyLevel.Full)
            return true;

        // unknown never auto-allows except under Full (handled above).
        if (commandClass == CommandClass.Unknown)
            return false;

        return level switch
        {
            PolicyLevel.Read => commandClass == CommandClass.Read,
            PolicyLevel.Trusted => commandClass is CommandClass.Read or CommandClass.Write,
            // Deny and any unexpected level: never auto-allow.
            _ => false,
        };
    }

    /// <summary>
    /// Auto-allow, or require human decision (prompt if UI available, else block).
    /// </summary>
    public static PolicyDecision Decide(PolicyLevel level, CommandClass commandClass) =>
        IsAutoAllowed(level, commandClass)
            ? PolicyDecision.AutoAllow
            : PolicyDecision.NeedsApproval;
}

/// <summary>
/// Result of evaluating a tool × launcher policy against a command class.
/// </summary>
public enum PolicyDecision
{
    /// <summary>Secret release / action may proceed without UI.</summary>
    AutoAllow = 0,

    /// <summary>
    /// Not auto-allowed. Approval Gate may prompt; if no UI, Session Agent blocks.
    /// </summary>
    NeedsApproval = 1,
}
