namespace CmdWarden.Contracts;

/// <summary>
/// One clock for the Approval Gate. The desktop card waits <see cref="Gate"/> for a click.
/// A client that can hit the gate waits <see cref="Client"/>, so the deny result reaches it.
/// </summary>
public static class ApprovalGateTimeouts
{
    public static readonly TimeSpan Gate = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Client = Gate + TimeSpan.FromSeconds(30);
}
