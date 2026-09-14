namespace CmdWarden.Contracts;

/// <summary>Product-owned process names around the credential helpers (#202, #206).</summary>
public static class HelperTools
{
    public const string DockerHelperExe = "docker-credential-cmdwarden.exe";
    public const string GitHelperExe = "git-credential-cmdwarden.exe";

    /// <summary>docker CLI plugins that sit between the helper and the real docker.exe.</summary>
    public static readonly IReadOnlySet<string> DockerPlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "docker-compose.exe",
        "docker-buildx.exe",
    };

    public static bool IsDockerPlugin(string? fileName) => fileName is not null && DockerPlugins.Contains(fileName);
}
