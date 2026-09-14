using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Locates built binaries for process-level tests.
/// Prefers the configuration matching the test host (Release in CI, Debug locally), then the other.
/// </summary>
internal static class TestPaths
{
    private const string Tfms = "net10.0";

    public static string RepoRoot { get; } = FindRepoRoot();

    public static string FindApprovalGateExe()
    {
        const string windowsTfm = "net10.0-windows";
        foreach (var config in PreferredConfigs())
        {
            var path = Path.Combine(
                RepoRoot, "src", "CmdWarden.ApprovalGate", "bin", config, windowsTfm,
                "CmdWarden.ApprovalGate.exe");
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException(
            "CmdWarden.ApprovalGate.exe not found under bin/Release or bin/Debug. Build the solution before running tests.",
            Path.Combine(RepoRoot, "src", "CmdWarden.ApprovalGate", "bin"));
    }

    public static string FindAgentDll()
    {
        foreach (var config in PreferredConfigs())
        {
            var path = Path.Combine(
                RepoRoot, "src", "CmdWarden.Agent", "bin", config, Tfms, "CmdWarden.Agent.dll");
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException(
            "CmdWarden.Agent.dll not found under bin/Release or bin/Debug. Build the solution before running tests.",
            Path.Combine(RepoRoot, "src", "CmdWarden.Agent", "bin"));
    }

    public static string FindAzShimOutputDir() =>
        FindShimOutputDir("CmdWarden.Shim.Az", "az");

    public static string FindDockerShimOutputDir() =>
        FindShimOutputDir("CmdWarden.Shim.Docker", "docker");

    public static string FindGitShimOutputDir() =>
        FindShimOutputDir("CmdWarden.Shim.Git", "git");

    public static string FindGitHelperOutputDir() =>
        FindShimOutputDir("CmdWarden.Helper.Git", "git-credential-cmdwarden");

    public static string FindGitHelperExe()
    {
        var dir = FindGitHelperOutputDir();
        var exe = Path.Combine(dir, HelperTools.GitHelperExe);
        if (File.Exists(exe))
            return exe;
        throw new FileNotFoundException("git-credential-cmdwarden.exe not found. Build CmdWarden.Helper.Git.", exe);
    }

    public static string FindDockerHelperOutputDir() =>
        FindShimOutputDir("CmdWarden.Helper.Docker", "docker-credential-cmdwarden");

    public static string FindGhShimOutputDir() =>
        FindShimOutputDir("CmdWarden.Shim.Gh", "gh");

    public static string FindShimOutputDir(string projectFolder, string toolName)
    {
        foreach (var config in PreferredConfigs())
        {
            var path = Path.Combine(RepoRoot, "src", projectFolder, "bin", config, Tfms);
            if (File.Exists(Path.Combine(path, $"{toolName}.exe")) ||
                File.Exists(Path.Combine(path, $"{toolName}.dll")))
                return path;
        }

        throw new FileNotFoundException(
            $"Build {projectFolder} first (Release or Debug).",
            Path.Combine(RepoRoot, "src", projectFolder, "bin"));
    }

    /// <summary>
    /// Prefer the configuration of the running test assembly, then the alternate.
    /// CI builds Release; local defaults are usually Debug.
    /// </summary>
    private static string[] PreferredConfigs()
    {
        var baseDir = AppContext.BaseDirectory;
        if (ContainsConfigSegment(baseDir, "Release"))
            return ["Release", "Debug"];
        if (ContainsConfigSegment(baseDir, "Debug"))
            return ["Debug", "Release"];
        // Unknown layout: try both (Release first for CI parity).
        return ["Release", "Debug"];
    }

    private static bool ContainsConfigSegment(string path, string config)
    {
        var sep = Path.DirectorySeparatorChar;
        var alt = Path.AltDirectorySeparatorChar;
        return path.Contains($"{sep}{config}{sep}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{alt}{config}{alt}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{sep}{config}{alt}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{alt}{config}{sep}", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CmdWarden.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate CmdWarden.sln from test base directory.");
    }
}
