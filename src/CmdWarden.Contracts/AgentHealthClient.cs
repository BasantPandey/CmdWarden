using Grpc.Core;
using Grpc.Net.Client;
using CmdWarden.Contracts.Grpc;

namespace CmdWarden.Contracts;

public static class AgentHealthClient
{
    public static async Task<HealthResponse> GetHealthAsync(
        string? pipeName = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var channel = AgentChannelFactory.Create(pipeName, timeout);
        var client = new SessionAgent.SessionAgentClient(channel);
        using var callCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        callCts.CancelAfter(timeout ?? TimeSpan.FromSeconds(2));
        return await client.GetHealthAsync(new HealthRequest(), cancellationToken: callCts.Token)
            .ConfigureAwait(false);
    }

    public static async Task<CallerIdentityResponse> ResolveIdentityAsync(
        string? pipeName = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var channel = AgentChannelFactory.Create(pipeName, timeout);
        var client = new SessionAgent.SessionAgentClient(channel);
        using var callCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        callCts.CancelAfter(timeout ?? TimeSpan.FromSeconds(5));
        return await client.ResolveCallerIdentityAsync(
            new CallerIdentityRequest(),
            cancellationToken: callCts.Token).ConfigureAwait(false);
    }

    public static bool IsAgentUnreachable(Exception ex) =>
        ex is RpcException
        {
            StatusCode: StatusCode.Unavailable
                or StatusCode.DeadlineExceeded
                or StatusCode.Internal
                or StatusCode.Cancelled
                or StatusCode.Unknown
        }
        or OperationCanceledException
        or TimeoutException
        or IOException
        or HttpRequestException;
}
