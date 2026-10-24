using System.IO.Pipes;
using System.Security.Principal;
using Grpc.Net.Client;

namespace CmdWarden.Contracts;

/// <summary>
/// Builds a gRPC channel over a Windows named pipe. It connects only to a pipe that this user owns.
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
    /// Identification lets the agent read the account of the caller; it cannot act as the caller.
    /// </summary>
    internal static NamedPipeClientStream CreateClientStream(string pipeName) =>
        new(
            serverName: ".",
            pipeName: pipeName,
            direction: PipeDirection.InOut,
            options: PipeOptions.Asynchronous,
            impersonationLevel: TokenImpersonationLevel.Identification);

    /// <summary>
    /// Refuse a pipe that another account owns. An agent account (#36) talks to the agent of the
    /// person it works for, so it checks for that owner.
    /// #97: PipeOptions.CurrentUserOnly compares the owner with the default owner of the token. For
    /// an elevated admin that is the Administrators group, but Git for Windows sets it to the user in
    /// the helpers that it starts. So a helper and the agent, at the same elevation, did not connect.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void CheckServerOwner(NamedPipeClientStream client)
    {
        var owner = client.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
        if (AgentEndpoints.RunsAsOtherAccount)
        {
            var expected = AgentAccounts.TryFind(AgentEndpoints.OwnerUserName);
            if (expected is null || !expected.Equals(owner))
                throw new UnauthorizedAccessException(
                    $"The Session Agent pipe is not owned by {AgentEndpoints.OwnerUserName}. {ProductInfo.Name} does not connect to it.");
            return;
        }
        if (!IsOwnPipe(owner))
            throw new UnauthorizedAccessException($"The Session Agent pipe is not owned by this user. {ProductInfo.Name} does not connect to it.");
    }

    /// <summary>
    /// The owner is this user, the default owner of this token, or, for an elevated caller only, the
    /// Administrators group: the owner of a pipe that an elevated process makes.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static bool IsOwnPipe(IdentityReference? owner)
    {
        if (owner is null)
            return false;
        using var me = WindowsIdentity.GetCurrent();
        return owner.Equals(me.User) || owner.Equals(me.Owner)
            || (owner.Equals(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null))
                && new WindowsPrincipal(me).IsInRole(WindowsBuiltInRole.Administrator));
    }
}
