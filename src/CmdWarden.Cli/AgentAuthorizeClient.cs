using Grpc.Core;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Grpc;

namespace CmdWarden.Cli;

public static class AgentAuthorizeClient
{
    public static async Task<AuthorizeResponse> AuthorizeAsync(
        string tool,
        IEnumerable<string> argv,
        string? secretName = null,
        string? pipeName = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? callerEnv = null)
    {
        using var channel = AgentChannelFactory.Create(pipeName, timeout);
        var client = new SessionAgent.SessionAgentClient(channel);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));

        var request = new AuthorizeRequest
        {
            Tool = tool,
            SecretName = secretName ?? "",
        };
        request.Argv.AddRange(argv);
        foreach (var (key, value) in callerEnv ?? new Dictionary<string, string>())
            request.CallerEnv[key] = value;

        return await client.AuthorizeAsync(request, cancellationToken: cts.Token).ConfigureAwait(false);
    }

    public static bool IsDenied(Exception ex) =>
        ex is RpcException { StatusCode: StatusCode.PermissionDenied or StatusCode.FailedPrecondition };
}
