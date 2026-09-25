using System.Runtime.Versioning;
using CmdWarden.Agent.Identity;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace CmdWarden.Agent;

/// <summary>
/// #36: an agent account reaches the pipe only after the person enrolls it, and then only the gate
/// calls. It never saves, deletes, or lists vault secrets, never changes grants, and never probes the
/// leak guard with guesses.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class OwnerOnlyInterceptor : Interceptor
{
    public static readonly IReadOnlySet<string> AgentAccountMethods = new HashSet<string>(StringComparer.Ordinal)
    {
        "GetHealth", "ResolveCallerIdentity", "Authorize", "ReleaseSecret", "HelperCredential", "CheckPolicy",
    };

    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var method = context.Method[(context.Method.LastIndexOf('/') + 1)..];
        if (!AgentAccountMethods.Contains(method) && PipeCaller.IsForeign(context.GetHttpContext()))
            throw new RpcException(new Status(StatusCode.PermissionDenied,
                $"An agent account may not call {method}. Only the person who runs the Session Agent may."));
        return continuation(request, context);
    }
}
