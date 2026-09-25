namespace CmdWarden.Contracts.Scan;

/// <summary>
/// Environment variables that carry a credential, or point a tool past its shim, around the gate.
/// The scan detectors report them; <c>cw launch</c> removes them from a harness (#25).
/// </summary>
public static class AmbientEnv
{
    public static readonly IReadOnlyList<string> GhTokens = ["GH_TOKEN", "GITHUB_TOKEN", "GH_ENTERPRISE_TOKEN", "GITHUB_ENTERPRISE_TOKEN"];
    public const string GhPath = "GH_PATH";
    public static readonly IReadOnlyList<string> AzServicePrincipal = ["AZURE_CLIENT_SECRET", "AZURE_CLIENT_CERTIFICATE_PATH", "AZURE_FEDERATED_TOKEN_FILE"];
    public const string DockerAuthConfig = "DOCKER_AUTH_CONFIG";

    /// <summary>Every name above: what a clean harness start removes.</summary>
    public static readonly IReadOnlyList<string> All = [.. GhTokens, GhPath, .. AzServicePrincipal, DockerAuthConfig];
}
