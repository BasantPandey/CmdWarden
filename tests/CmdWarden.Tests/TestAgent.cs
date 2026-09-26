using System.Diagnostics;
using CmdWarden.Agent.Approval;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// A Session Agent process with its own pipe, product root and policy file. This test process
/// uses the same values through its env while the agent runs. Dispose stops the agent and deletes
/// the product root.
/// </summary>
internal sealed class TestAgent : IAsyncDisposable
{
    private readonly Process _process;

    public string PipeName { get; }
    public string ProductRoot { get; }
    public string PolicyPath => Path.Combine(ProductRoot, "policy.json");
    public string SelectedPolicyKey { get; private set; } = "";

    private TestAgent(Process process, string pipeName, string productRoot)
    {
        _process = process;
        PipeName = pipeName;
        ProductRoot = productRoot;
    }

    /// <summary>Null when this test host is not a launcher that policy can auto-allow.</summary>
    public static async Task<TestAgent?> StartAsync(string approvalMode = "off", string? productRoot = null,
        IReadOnlyDictionary<string, string>? env = null)
    {
        var pipeName = $"{AgentEndpoints.PipeNamePrefix}-test-{Guid.NewGuid():N}";
        productRoot ??= Path.Combine(Path.GetTempPath(), "cw-prod-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(productRoot);
        var policyPath = Path.Combine(productRoot, "policy.json");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { TestPaths.FindAgentDll() },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.Environment["CW_PIPE_NAME"] = pipeName;
        psi.Environment["CW_POLICY_PATH"] = policyPath;
        psi.Environment[ProductPaths.EnvVar] = productRoot;
        psi.Environment[ApprovalGateFactory.EnvVar] = approvalMode;
        foreach (var (key, value) in env ?? new Dictionary<string, string>())
            psi.Environment[key] = value;

        Environment.SetEnvironmentVariable("CW_PIPE_NAME", pipeName);
        Environment.SetEnvironmentVariable("CW_POLICY_PATH", policyPath);
        Environment.SetEnvironmentVariable(ProductPaths.EnvVar, productRoot);

        var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start agent.");
        var agent = new TestAgent(process, pipeName, productRoot);
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (true)
        {
            if (process.HasExited)
            {
                var output = await process.StandardOutput.ReadToEndAsync() + await process.StandardError.ReadToEndAsync();
                await agent.DisposeAsync();
                throw new InvalidOperationException($"Agent exited: {output}");
            }
            try
            {
                _ = await AgentHealthClient.GetHealthAsync(pipeName, TimeSpan.FromMilliseconds(500));
                break;
            }
            catch
            {
                if (DateTime.UtcNow > deadline)
                {
                    await agent.DisposeAsync();
                    throw new TimeoutException("Agent did not become healthy.");
                }
                await Task.Delay(150);
            }
        }

        var id = await AgentHealthClient.ResolveIdentityAsync(pipeName);
        if (!id.AutoApproveEligible)
        {
            await agent.DisposeAsync();
            return null;
        }
        agent.SelectedPolicyKey = id.SelectedPolicyKey;
        return agent;
    }

    public void Enroll(LauncherEnrollmentKind kind)
    {
        var store = new PolicyStore(PolicyPath);
        store.Load();
        store.Enroll(SelectedPolicyKey, kind);
        store.Save();
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
        Environment.SetEnvironmentVariable("CW_PIPE_NAME", null);
        Environment.SetEnvironmentVariable("CW_POLICY_PATH", null);
        Environment.SetEnvironmentVariable(ProductPaths.EnvVar, null);
        try { Directory.Delete(ProductRoot, recursive: true); } catch { /* A shim can still hold a file. */ }
    }
}
