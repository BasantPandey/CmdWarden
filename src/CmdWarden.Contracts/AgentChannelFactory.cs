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
                    if (AgentEndpoints.RunsAsOtherAccount)
                        CheckServerOwner(client);
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

    /// <summary>
    /// CurrentUserOnly makes the client refuse a pipe that another account owns. An agent account (#36)
    /// talks to the agent of the person it works for, so it checks that owner by hand instead.
    /// Identification lets the agent read the account of the caller; it cannot act as the caller.
    /// </summary>
    internal static NamedPipeClientStream CreateClientStream(string pipeName) =>
        new(
            serverName: ".",
            pipeName: pipeName,
            direction: PipeDirection.InOut,
            options: AgentEndpoints.RunsAsOtherAccount
                ? PipeOptions.Asynchronous
                : PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            impersonationLevel: TokenImpersonationLevel.Identification);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void CheckServerOwner(NamedPipeClientStream client)
    {
        var expected = AgentAccounts.TryFind(AgentEndpoints.OwnerUserName);
        var owner = client.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
        if (expected is null || !expected.Equals(owner))
            throw new UnauthorizedAccessException(
                $"The Session Agent pipe is not owned by {AgentEndpoints.OwnerUserName}. {ProductInfo.Name} does not connect to it.");
    }
}
