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
        string? userProfile = null,
        string? workingDirectory = null,
        string? appData = null)
    {
        ProductRoot = productRoot ?? ProductPaths.Root();
        PathEnv = pathEnv ?? Environment.GetEnvironmentVariable("PATH") ?? "";
        GetEnv = getEnv ?? (static name => Environment.GetEnvironmentVariable(name));
        UserProfile = userProfile
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            ?? Environment.GetEnvironmentVariable("USERPROFILE")
            ?? "";
        WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory;
        AppData = appData ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    }

    /// <summary>Project config files (.mcp.json, .cursor/mcp.json) are read from here (#28).</summary>
    public string WorkingDirectory { get; }

    /// <summary>Roaming AppData: Claude Desktop and VS Code keep their MCP config here (#28).</summary>
    public string AppData { get; }

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
