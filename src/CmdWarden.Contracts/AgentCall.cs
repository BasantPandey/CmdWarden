using Grpc.Net.Client;
using CmdWarden.Contracts.Grpc;

namespace CmdWarden.Contracts;

/// <summary>
/// Bundles the per-call gRPC channel, client, and deadline so each agent RPC method
/// needs one line to set up. Shared by the static agent clients.
/// </summary>
internal readonly struct AgentCall : IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly CancellationTokenSource _cts;

    public SessionAgent.SessionAgentClient Client { get; }
    public CancellationToken Token => _cts.Token;

    private AgentCall(GrpcChannel channel, CancellationTokenSource cts)
    {
        _channel = channel;
        _cts = cts;
        Client = new SessionAgent.SessionAgentClient(channel);
    }

    public static AgentCall Create(string? pipeName, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var channel = AgentChannelFactory.Create(pipeName, timeout);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(10));
        return new AgentCall(channel, cts);
    }

    public void Dispose()
    {
        _cts.Dispose();
        _channel.Dispose();
    }
}
