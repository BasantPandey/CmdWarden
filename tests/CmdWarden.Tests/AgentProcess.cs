using System.Diagnostics;
using CmdWarden.Agent.Approval;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// A Session Agent process for a test, on its own pipe. It reads the output of the agent all the
/// time: a full output pipe blocks the agent on its next log line, and the agent then stops
/// answering (#97). The last lines go in the error when the agent does not start.
/// </summary>
internal sealed class AgentProcess : IAsyncDisposable
{
    private const int KeptLines = 200;
    private readonly Process _process;
    private readonly Queue<string> _output = new();

    private AgentProcess(Process process) => _process = process;

    public bool HasExited => _process.HasExited;

    public string Output
    {
        get
        {
            lock (_output)
                return string.Join(Environment.NewLine, _output);
        }
    }

    /// <param name="policyPath">Null: a new product root in the temp folder holds the policy file.</param>
    /// <param name="productRoot">Null: the folder of <paramref name="policyPath"/>.</param>
    public static async Task<AgentProcess> StartAsync(
        string agentDll,
        string pipeName,
        string? policyPath = null,
        string? productRoot = null,
        string approvalMode = "off",
        string? helperPath = null,
        IReadOnlyDictionary<string, string>? env = null)
    {
        productRoot ??= policyPath is null
            ? Path.Combine(Path.GetTempPath(), "cw-prod-" + Guid.NewGuid().ToString("N"))
            : Path.GetDirectoryName(policyPath)!;
        policyPath ??= Path.Combine(productRoot, "policy.json");
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { agentDll },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.Environment["CW_PIPE_NAME"] = pipeName;
        psi.Environment["CW_POLICY_PATH"] = policyPath;
        psi.Environment[ProductPaths.EnvVar] = productRoot;
        psi.Environment[ApprovalGateFactory.EnvVar] = approvalMode;
        if (helperPath is not null)
            psi.Environment[ProcessApprovalGate.HelperPathEnvVar] = helperPath;
        foreach (var (key, value) in env ?? new Dictionary<string, string>())
            psi.Environment[key] = value;

        var agent = new AgentProcess(Process.Start(psi) ?? throw new InvalidOperationException("Failed to start agent."));
        agent._process.OutputDataReceived += (_, e) => agent.Keep(e.Data);
        agent._process.ErrorDataReceived += (_, e) => agent.Keep(e.Data);
        agent._process.BeginOutputReadLine();
        agent._process.BeginErrorReadLine();

        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (true)
        {
            if (agent._process.HasExited)
            {
                await agent._process.WaitForExitAsync();
                var exit = agent._process.ExitCode;
                await agent.DisposeAsync();
                throw new InvalidOperationException($"Agent exited with {exit}: {agent.Output}");
            }
            try
            {
                _ = await AgentHealthClient.GetHealthAsync(pipeName, TimeSpan.FromMilliseconds(500));
                return agent;
            }
            catch
            {
                if (DateTime.UtcNow > deadline)
                {
                    await agent.DisposeAsync();
                    throw new TimeoutException($"Agent did not become healthy. Its last output:{Environment.NewLine}{agent.Output}");
                }
                await Task.Delay(150);
            }
        }
    }

    private void Keep(string? line)
    {
        if (line is null)
            return;
        lock (_output)
        {
            _output.Enqueue(line);
            while (_output.Count > KeptLines)
                _output.Dequeue();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }
        catch
        {
            // The agent is gone.
        }
        _process.Dispose();
    }
}
