using Google.Protobuf;
using Grpc.Core;
using CmdWarden.Contracts.Grpc;

namespace CmdWarden.Contracts;

public static class AgentVaultClient
{
    public static async Task<SaveSecretResponse> SaveAsync(
        string name,
        byte[] value,
        string? pipeName = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var call = AgentCall.Create(pipeName, timeout, cancellationToken);
        return await call.Client.SaveSecretAsync(
            new SaveSecretRequest { Name = name, Value = ByteString.CopyFrom(value) },
            cancellationToken: call.Token).ConfigureAwait(false);
    }

    public static async Task<ReleaseSecretResponse> ReleaseAsync(
        string name,
        string purpose = "inject",
        string? pipeName = null,
        TimeSpan? timeout = null,
        string tool = "inject",
        string commandClass = "write",
        string? commandLine = null,
        CancellationToken cancellationToken = default,
        IEnumerable<string>? boundPaths = null)
    {
        using var call = AgentCall.Create(pipeName, timeout, cancellationToken);
        var request = new ReleaseSecretRequest
        {
            Name = name,
            Purpose = purpose,
            Tool = tool,
            CommandClass = commandClass,
            CommandLine = commandLine ?? "",
        };
        request.BoundPaths.AddRange(boundPaths ?? []);
        return await call.Client.ReleaseSecretAsync(request, cancellationToken: call.Token).ConfigureAwait(false);
    }

    public static bool IsPermissionDenied(Exception ex) =>
        ex is RpcException { StatusCode: StatusCode.PermissionDenied };

    /// <summary>The audit row does not persist, so the agent withholds the value (#199).</summary>
    public static bool IsReleaseBlocked(Exception ex) =>
        ex is RpcException { StatusCode: StatusCode.FailedPrecondition } rpc
        && rpc.Status.Detail.StartsWith(PolicyReasonCodes.ApprovalUnavailable + ": audit write failed", StringComparison.Ordinal);

    public static async Task<bool> DeleteAsync(
        string name,
        string? pipeName = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var call = AgentCall.Create(pipeName, timeout, cancellationToken);
        var response = await call.Client.DeleteSecretAsync(
            new DeleteSecretRequest { Name = name },
            cancellationToken: call.Token).ConfigureAwait(false);
        return response.Deleted;
    }

    public static bool IsNotFound(Exception ex) =>
        ex is RpcException { StatusCode: StatusCode.NotFound };

    public static async Task<IReadOnlyList<string>> ListSecretNamesAsync(
        string? pipeName = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var call = AgentCall.Create(pipeName, timeout, cancellationToken);
        var response = await call.Client.ListSecretNamesAsync(
            new ListSecretNamesRequest(),
            cancellationToken: call.Token).ConfigureAwait(false);
        return response.Names;
    }
}
