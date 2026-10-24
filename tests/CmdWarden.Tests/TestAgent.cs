using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// A Session Agent process with its own pipe, product root and policy file. This test process
/// uses the same values through its env while the agent runs. Dispose stops the agent and deletes
/// the product root.
/// </summary>
internal sealed class TestAgent : IAsyncDisposable
{
    private readonly AgentProcess _process;

    public string PipeName { get; }
    public string ProductRoot { get; }
    public string PolicyPath => Path.Combine(ProductRoot, "policy.json");
    public string SelectedPolicyKey { get; private set; } = "";

    private TestAgent(AgentProcess process, string pipeName, string productRoot)
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

        Environment.SetEnvironmentVariable("CW_PIPE_NAME", pipeName);
        Environment.SetEnvironmentVariable("CW_POLICY_PATH", policyPath);
        Environment.SetEnvironmentVariable(ProductPaths.EnvVar, productRoot);

        AgentProcess process;
        try
        {
            process = await AgentProcess.StartAsync(TestPaths.FindAgentDll(), pipeName, policyPath, productRoot, approvalMode, env: env);
        }
        catch
        {
            ClearEnvironment();
            throw;
        }
        var agent = new TestAgent(process, pipeName, productRoot);

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
        await _process.DisposeAsync();
        ClearEnvironment();
        try { Directory.Delete(ProductRoot, recursive: true); } catch { /* A shim can still hold a file. */ }
    }

    private static void ClearEnvironment()
    {
        Environment.SetEnvironmentVariable("CW_PIPE_NAME", null);
        Environment.SetEnvironmentVariable("CW_POLICY_PATH", null);
        Environment.SetEnvironmentVariable(ProductPaths.EnvVar, null);
    }
}
