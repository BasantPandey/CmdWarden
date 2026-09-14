using CmdWarden.Contracts.Grpc;

namespace CmdWarden.Contracts;

/// <summary>MigrateToolStore client (#208): the gh shim calls it after a granted login/refresh/logout exits 0.</summary>
public static class AgentMigrateClient
{
    public static async Task<MigrateToolStoreResponse> MigrateAsync(
        string tool,
        IEnumerable<string> argv,
        string? pipeName = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var call = AgentCall.Create(pipeName, timeout, cancellationToken);
        var request = new MigrateToolStoreRequest { Tool = tool };
        request.Argv.AddRange(argv);
        return await call.Client.MigrateToolStoreAsync(request, cancellationToken: call.Token).ConfigureAwait(false);
    }
}
