using CmdWarden.Contracts.Grpc;

namespace CmdWarden.Contracts;

/// <summary>Client side of the credential helper gate (#202).</summary>
public static class AgentHelperClient
{
    public static async Task<HelperCredentialResponse> CredentialAsync(
        string tool,
        string action,
        string serverUrl = "",
        string username = "",
        string secret = "",
        string? pipeName = null,
        TimeSpan? timeout = null,
        string passwordExpiryUtc = "",
        string oauthRefreshToken = "",
        bool ephemeral = false,
        CancellationToken cancellationToken = default)
    {
        using var call = AgentCall.Create(pipeName, timeout, cancellationToken);
        return await call.Client.HelperCredentialAsync(
            new HelperCredentialRequest
            {
                Tool = tool,
                Action = action,
                ServerUrl = serverUrl,
                Username = username,
                Secret = secret,
                PasswordExpiryUtc = passwordExpiryUtc,
                OauthRefreshToken = oauthRefreshToken,
                Ephemeral = ephemeral,
            },
            cancellationToken: call.Token).ConfigureAwait(false);
    }
}
