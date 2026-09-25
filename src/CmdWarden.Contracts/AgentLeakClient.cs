using CmdWarden.Contracts.Grpc;

namespace CmdWarden.Contracts;

/// <summary>Client for the leak guard (#27). The Agent returns the texts with placeholders, never values.</summary>
public static class AgentLeakClient
{
    public static async Task<CheckLeakResponse> CheckAsync(
        IEnumerable<string> texts,
        string source,
        string? pipeName = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var call = AgentCall.Create(pipeName, timeout, cancellationToken);
        var request = new CheckLeakRequest { Source = source };
        request.Texts.AddRange(texts);
        return await call.Client.CheckLeakAsync(request, cancellationToken: call.Token).ConfigureAwait(false);
    }
}
