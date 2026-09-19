using Grpc.Core;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Grpc;

return await GitCredentialHelperApp.RunAsync(args);

/// <summary>
/// git-credential-cmdwarden: git line protocol in, HelperCredential RPC out (#206).
/// </summary>
public static class GitCredentialHelperApp
{
    public static async Task<int> RunAsync(
        string[] args,
        TextReader? stdin = null,
        TextWriter? stdout = null,
        TextWriter? stderr = null,
        string? pipeName = null,
        TimeSpan? timeout = null)
    {
        stdin ??= Console.In;
        stdout ??= Console.Out;
        stderr ??= Console.Error;

        var action = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "";
        if (action is not "get" and not "store" and not "erase")
            return 0;

        var attrs = GitCredentialProtocol.Parse(stdin);
        var serverUrl = attrs.ServerUrl;
        if (serverUrl.Length == 0)
        {
            if (action == "get")
                GitCredentialProtocol.WriteDeny(stdout);
            return 0;
        }

        try
        {
            await TryEnsureAgentAsync(pipeName).ConfigureAwait(false);

            var response = await AgentHelperClient.CredentialAsync(
                GitVaultNames.Tool,
                action,
                serverUrl,
                attrs.Username,
                attrs.Password,
                pipeName,
                timeout ?? ApprovalGateTimeouts.Client,
                attrs.PasswordExpiryUtc,
                attrs.OauthRefreshToken,
                attrs.Ephemeral).ConfigureAwait(false);

            if (action == "get")
                GitCredentialProtocol.WriteGet(
                    stdout,
                    attrs,
                    response.Username,
                    response.Secret,
                    response.PasswordExpiryUtc,
                    response.OauthRefreshToken);

            return 0;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return 0;
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.PermissionDenied or StatusCode.FailedPrecondition)
        {
            if (action == "get")
            {
                GitCredentialProtocol.WriteDeny(stdout);
                stderr.WriteLine(GitCredentialProtocol.DenyStderr(serverUrl, Reason(ex)));
            }

            return 0;
        }
        catch (Exception ex)
        {
            if (action == "get")
            {
                GitCredentialProtocol.WriteDeny(stdout);
                stderr.WriteLine(GitCredentialProtocol.DenyStderr(serverUrl, PolicyReasonCodes.AgentDown));
                stderr.WriteLine("DIAG pipe=" + Environment.GetEnvironmentVariable("CW_PIPE_NAME"));
                stderr.WriteLine("DIAG owner=" + System.Security.Principal.WindowsIdentity.GetCurrent().Owner + " user=" + System.Security.Principal.WindowsIdentity.GetCurrent().User);
                stderr.WriteLine("DIAG " + ex);
                if (ex is RpcException rpc)
                    stderr.WriteLine("DIAG debug=" + rpc.Status.DebugException);
            }

            return 0;
        }
    }

    private static async Task TryEnsureAgentAsync(string? pipeName)
    {
        try
        {
            _ = await AgentLifecycle.EnsureRunningAsync(pipeName).ConfigureAwait(false);
        }
        catch
        {
            // fail closed later on the RPC
        }
    }

    private static string Reason(RpcException ex)
    {
        var detail = ex.Status.Detail ?? "";
        var colon = detail.IndexOf(':');
        return colon > 0 ? detail[..colon] : (detail.Length > 0 ? detail : PolicyReasonCodes.AgentDown);
    }
}
