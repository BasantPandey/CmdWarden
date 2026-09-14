using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Make the lazy agent start fail fast: CW_AGENT_PATH points at an apphost stub with no
/// CmdWarden.Agent.dll beside it. Without this, an "agent down" test starts a real agent that
/// outlives the test run and locks the build output.
/// </summary>
internal sealed class NoAgentBinary : IDisposable
{
    private readonly string? _previous = Environment.GetEnvironmentVariable(AgentLocator.EnvAgentPath);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cw-no-agent-" + Guid.NewGuid().ToString("N"));

    public NoAgentBinary()
    {
        Directory.CreateDirectory(_dir);
        var stub = Path.Combine(_dir, "CmdWarden.Agent.exe");
        File.WriteAllBytes(stub, [0]);
        Environment.SetEnvironmentVariable(AgentLocator.EnvAgentPath, stub);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(AgentLocator.EnvAgentPath, _previous);
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }
}
