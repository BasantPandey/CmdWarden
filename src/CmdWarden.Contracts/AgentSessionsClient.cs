using CmdWarden.Contracts.Grpc;

namespace CmdWarden.Contracts;

/// <summary>
/// Client for the Session Agent's "Allow for session" list/revoke RPCs (#134).
/// Read-only view plus revoke; never prompts the Approval Gate, never carries secret values.
/// Follows the <see cref="AgentVaultClient"/> per-call channel pattern.
/// </summary>
public static class AgentSessionsClient
{
    public static async Task<IReadOnlyList<SessionAllowRow>> ListAsync(
        string? pipeName = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var call = AgentCall.Create(pipeName, timeout, cancellationToken);
        var response = await call.Client.ListSessionAllowsAsync(
            new ListSessionAllowsRequest(),
            cancellationToken: call.Token).ConfigureAwait(false);
        return response.Rows;
    }

    /// <param name="id">Grant id to revoke; ignored when <paramref name="all"/> is true.</param>
    /// <returns>Number of grants removed.</returns>
    public static async Task<int> RevokeAsync(
        string? id,
        bool all,
        string? pipeName = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var call = AgentCall.Create(pipeName, timeout, cancellationToken);
        var response = await call.Client.RevokeSessionAllowAsync(
            new RevokeSessionAllowRequest { Id = id ?? "", All = all },
            cancellationToken: call.Token).ConfigureAwait(false);
        return response.Removed;
    }
}
