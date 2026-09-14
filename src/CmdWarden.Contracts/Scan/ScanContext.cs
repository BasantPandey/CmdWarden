namespace CmdWarden.Contracts.Scan;

/// <summary>
/// Read-only inputs for detectors. Injectable for tests with isolated product root / env.
/// </summary>
public sealed class ScanContext
{
    public ScanContext(
        string? productRoot = null,
        string? pathEnv = null,
        Func<string, string?>? getEnv = null,
        string? userProfile = null)
    {
        ProductRoot = productRoot ?? ProductPaths.Root();
        PathEnv = pathEnv ?? Environment.GetEnvironmentVariable("PATH") ?? "";
        GetEnv = getEnv ?? (static name => Environment.GetEnvironmentVariable(name));
        UserProfile = userProfile
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            ?? Environment.GetEnvironmentVariable("USERPROFILE")
            ?? "";
    }

    public string ProductRoot { get; }
    public string PathEnv { get; }
    public Func<string, string?> GetEnv { get; }
    public string UserProfile { get; }

    public string ShimsDir => Path.Combine(ProductRoot, "shims");
    public ToolPinStore Pins => new(ProductRoot);

    public bool EnvIsSet(string name)
    {
        var v = GetEnv(name);
        return !string.IsNullOrEmpty(v);
    }
}
