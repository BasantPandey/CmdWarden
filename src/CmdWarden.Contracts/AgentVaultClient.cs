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
        CancellationToken cancellationToken = default,
        string? projectPath = null,
        string? account = null,
        string? sites = null)
    {
        using var call = AgentCall.Create(pipeName, timeout, cancellationToken);
        var request = new SaveSecretRequest { Name = name, Value = ByteString.CopyFrom(value) };
        if (!string.IsNullOrWhiteSpace(projectPath))
            request.ProjectPath = projectPath;
        if (!string.IsNullOrWhiteSpace(account))
            request.Account = account;
        if (!string.IsNullOrWhiteSpace(sites))
            request.Sites = sites;
        return await call.Client.SaveSecretAsync(request, cancellationToken: call.Token).ConfigureAwait(false);
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
        IEnumerable<string>? boundPaths = null,
        IEnumerable<string>? extraNames = null)
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
        request.ExtraNames.AddRange(extraNames ?? []);
        request.AgentReason = AgentReason.FromEnvironment();
        request.WorkingDirectory = Environment.CurrentDirectory;
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
        CancellationToken cancellationToken = default,
        string? projectPath = null)
    {
        using var call = AgentCall.Create(pipeName, timeout, cancellationToken);
        var request = new DeleteSecretRequest { Name = name };
        if (!string.IsNullOrWhiteSpace(projectPath))
            request.ProjectPath = projectPath;
        var response = await call.Client.DeleteSecretAsync(request, cancellationToken: call.Token).ConfigureAwait(false);
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

    public static async Task<IReadOnlyList<LoginInfo>> ListLoginsAsync(
        string? pipeName = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var call = AgentCall.Create(pipeName, timeout, cancellationToken);
        var response = await call.Client.ListLoginsAsync(
            new ListLoginsRequest(),
            cancellationToken: call.Token).ConfigureAwait(false);
        return response.Logins;
    }
}
