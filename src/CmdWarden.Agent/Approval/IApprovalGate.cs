using CmdWarden.Contracts;

namespace CmdWarden.Agent.Approval;

/// <summary>
/// Native (or scripted) human decision when policy does not auto-allow.
/// </summary>
public interface IApprovalGate
{
    ApprovalOutcome Prompt(ApprovalRequest request);
}
