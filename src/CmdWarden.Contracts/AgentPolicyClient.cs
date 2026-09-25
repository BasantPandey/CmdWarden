using CmdWarden.Contracts.Grpc;

namespace CmdWarden.Contracts;

/// <summary>Client for CheckPolicy (#33): what Authorize would do, before the command runs.</summary>
public static class AgentPolicyClient
{
    public static async Task<CheckPolicyResponse> CheckAsync(
        string tool,
        IEnumerable<string> argv,
        string? pipeName = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default,
        string? workingDirectory = null)
    {
        using var call = AgentCall.Create(pipeName, timeout, cancellationToken);
        var request = new CheckPolicyRequest { Tool = tool, WorkingDirectory = workingDirectory ?? "" };
        request.Argv.AddRange(argv);
        return await call.Client.CheckPolicyAsync(request, cancellationToken: call.Token).ConfigureAwait(false);
    }
}
