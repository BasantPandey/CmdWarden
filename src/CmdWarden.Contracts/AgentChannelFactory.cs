using System.IO.Pipes;
using System.Security.Principal;
using Grpc.Net.Client;

namespace CmdWarden.Contracts;

/// <summary>
/// Builds a gRPC channel over a Windows named pipe with CurrentUserOnly client options.
/// </summary>
public static class AgentChannelFactory
{
    public static GrpcChannel Create(string? pipeName = null, TimeSpan? connectTimeout = null)
    {
        pipeName ??= AgentEndpoints.PipeName;
        var timeout = connectTimeout ?? TimeSpan.FromSeconds(2);

        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (context, cancellationToken) =>
            {
                var client = CreateClientStream(pipeName);
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectCts.CancelAfter(timeout);
                try
                {
                    await client.ConnectAsync(connectCts.Token).ConfigureAwait(false);
                }
                catch
                {
                    await client.DisposeAsync().ConfigureAwait(false);
                    throw;
                }

                return client;
            },
        };

        return GrpcChannel.ForAddress(
            AgentEndpoints.GrpcChannelAddress,
            new GrpcChannelOptions
            {
                HttpHandler = handler,
                DisposeHttpClient = true,
            });
    }

    internal static NamedPipeClientStream CreateClientStream(string pipeName) =>
        new(
            serverName: ".",
            pipeName: pipeName,
            direction: PipeDirection.InOut,
            options: PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            impersonationLevel: TokenImpersonationLevel.Anonymous);
}
