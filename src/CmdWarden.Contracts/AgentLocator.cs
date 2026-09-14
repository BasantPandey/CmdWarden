namespace CmdWarden.Contracts;

/// <summary>
/// Resolve Session Agent binary layout next to the CLI / tool install (issue #36 / #37).
/// </summary>
public static class AgentLocator
{
    public const string EnvAgentPath = "CW_AGENT_PATH";
    public const string AgentExeName = "CmdWarden.Agent.exe";
    public const string AgentDllName = "CmdWarden.Agent.dll";

    /// <summary>
    /// Prefer CW_AGENT_PATH, then agent/ beside the calling base directory, then sibling build output (dev).
    /// Returns path to .exe or .dll; null if not found.
    /// </summary>
    public static string? FindAgentBinary(string? baseDirectory = null)
    {
        var env = Environment.GetEnvironmentVariable(EnvAgentPath);
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env.Trim()))
            return Path.GetFullPath(env.Trim());

        var bases = new List<string>();
        if (!string.IsNullOrWhiteSpace(baseDirectory))
            bases.Add(baseDirectory);
        bases.Add(AppContext.BaseDirectory);

        foreach (var b in bases.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            // Prefer bundled agent/ next to cw (complete payload). Root dir may contain a
            // partial apphost copied by ProjectReference without CmdWarden.Agent.dll.
            var agentSub = Path.Combine(b, "agent");
            var hit = ProbeDir(agentSub);
            if (hit is not null)
                return hit;

            hit = ProbeDir(b);
            if (hit is not null)
                return hit;

            // Vault UI ships in secrets-manager/ next to agent/, one level below cw.
            hit = ProbeDir(Path.Combine(b, "..", "agent"));
            if (hit is not null)
                return Path.GetFullPath(hit);
        }

        // Dev: walk up for src/CmdWarden.Agent/bin/{Debug|Release}/net10.0/
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            foreach (var cfg in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(
                    dir.FullName, "src", "CmdWarden.Agent", "bin", cfg, "net10.0", AgentDllName);
                if (File.Exists(candidate))
                    return candidate;
                candidate = Path.Combine(
                    dir.FullName, "src", "CmdWarden.Agent", "bin", cfg, "net10.0", AgentExeName);
                if (File.Exists(candidate))
                    return candidate;
            }

            if (File.Exists(Path.Combine(dir.FullName, "CmdWarden.sln")))
                break;
            dir = dir.Parent;
        }

        return null;
    }

    public static string PidFilePath(string? productRoot = null) =>
        Path.Combine(productRoot ?? ProductPaths.Root(), "agent.pid");

    private static string? ProbeDir(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return null;

        // Prefer the managed entry assembly: framework-dependent apphost is useless without it.
        var dll = Path.Combine(dir, AgentDllName);
        if (File.Exists(dll))
            return dll;

        var exe = Path.Combine(dir, AgentExeName);
        // Self-contained-style layout only; skip orphan apphosts (no sibling dll).
        if (File.Exists(exe))
            return exe;

        return null;
    }
}
