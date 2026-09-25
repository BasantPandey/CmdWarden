using CmdWarden.Contracts;

namespace CmdWarden.Agent.Approval;

/// <summary>
/// Non-interactive gate for tests / headless CI (CW_APPROVAL_MODE=allow|deny).
/// </summary>
public sealed class ScriptedApprovalGate : IApprovalGate
{
    private readonly ApprovalOutcome _outcome;

    public ScriptedApprovalGate(ApprovalOutcome outcome)
    {
        if (outcome == ApprovalOutcome.Unavailable)
            throw new ArgumentOutOfRangeException(nameof(outcome), "Use FixedUnavailableGate or Unavailable outcome via factory.");
        _outcome = outcome;
    }

    public ApprovalAnswer Prompt(ApprovalRequest request) => _outcome;
}

/// <summary>
/// Always reports UI unavailable (fail closed without dialog).
/// </summary>
public sealed class UnavailableApprovalGate : IApprovalGate
{
    public ApprovalAnswer Prompt(ApprovalRequest request) => ApprovalOutcome.Unavailable;
}
