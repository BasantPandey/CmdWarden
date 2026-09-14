using System.Diagnostics;
using System.Text;
using CmdWarden.Agent.Approval;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Agent process with cmd.exe pinned as gh and the test host enrolled (default: AI Harness, Read).
/// Shared by the approval-memory process tests (#131, #132).
/// </summary>
internal sealed class ApprovalMemoryFixture : IAsyncDisposable
{
    public string PipeName { get; }
    public string ProductRoot { get; }
    public string PolicyPath { get; }
    public string SelectedPolicyKey { get; private set; } = "";
    private readonly string _approvalMode;
    private readonly IReadOnlyDictionary<string, string> _env;
    private Process _agent;

    private ApprovalMemoryFixture(
        string pipeName, string productRoot, string policyPath, string approvalMode,
        IReadOnlyDictionary<string, string> env, Process agent)
    {
        PipeName = pipeName;
        ProductRoot = productRoot;
        PolicyPath = policyPath;
        _approvalMode = approvalMode;
        _env = env;
        _agent = agent;
    }

    /// <param name="enroll">Null leaves the launcher unenrolled.</param>
    /// <param name="env">Extra agent environment, e.g. a shortened window.</param>
    public static async Task<ApprovalMemoryFixture?> CreateAsync(
        string approvalMode,
        LauncherEnrollmentKind? enroll = LauncherEnrollmentKind.AiHarness,
        IReadOnlyDictionary<string, string>? env = null)
    {
        var pipeName = $"{AgentEndpoints.PipeNamePrefix}-mem-{Guid.NewGuid():N}";
        var productRoot = Path.Combine(Path.GetTempPath(), "cw-prod-" + Guid.NewGuid().ToString("N"));
        var policyPath = Path.Combine(productRoot, "policy.json");
        Directory.CreateDirectory(productRoot);
        env ??= new Dictionary<string, string>();

        var agent = await StartAgentAsync(pipeName, productRoot, policyPath, approvalMode, env);
        var fx = new ApprovalMemoryFixture(pipeName, productRoot, policyPath, approvalMode, env, agent);
        var id = await AgentHealthClient.ResolveIdentityAsync(pipeName);
        if (!id.AutoApproveEligible)
        {
            await fx.DisposeAsync();
            return null;
        }

        fx.SelectedPolicyKey = id.SelectedPolicyKey;
        if (enroll is { } kind)
        {
            var store = new PolicyStore(policyPath);
            store.Load();
            store.Enroll(id.SelectedPolicyKey, kind);
            store.Save();
        }
        new ToolPinStore(productRoot).Save("gh", Path.Combine(Environment.SystemDirectory, "cmd.exe"));
        return fx;
    }

    /// <summary>Kill this fixture's agent process and start a fresh one on the same pipe, product
    /// root and policy file, so on-disk enrollment/policy/pins survive but in-memory approval
    /// memory does not (#133).</summary>
    public async Task RestartAsync()
    {
        try { if (!_agent.HasExited) _agent.Kill(entireProcessTree: true); } catch { /* ignore */ }
        await _agent.WaitForExitAsync();
        _agent.Dispose();
        _agent = await StartAgentAsync(PipeName, ProductRoot, PolicyPath, _approvalMode, _env);
    }

    private static async Task<Process> StartAgentAsync(
        string pipeName, string productRoot, string policyPath, string approvalMode,
        IReadOnlyDictionary<string, string> env)
    {
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
        foreach (var (k, v) in env)
            psi.Environment[k] = v;

        var agent = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start agent.");
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (true)
        {
            if (agent.HasExited)
                throw new InvalidOperationException("Agent exited: " + await agent.StandardError.ReadToEndAsync());
            try
            {
                _ = await AgentHealthClient.GetHealthAsync(pipeName, TimeSpan.FromMilliseconds(500));
                break;
            }
            catch
            {
                if (DateTime.UtcNow > deadline)
                {
                    try { agent.Kill(entireProcessTree: true); } catch { /* ignore */ }
                    throw new TimeoutException("Agent did not become healthy.");
                }
                await Task.Delay(150);
            }
        }

        return agent;
    }

    public void SetLevel(string tool, PolicyLevel level)
    {
        var store = new PolicyStore(PolicyPath);
        store.Load();
        store.SetLevel(SelectedPolicyKey, tool, level);
        store.Save();
    }

    public Task SaveTokenAsync(string value) =>
        AgentVaultClient.SaveAsync(SessionAgentServiceNames.GhToken, Encoding.UTF8.GetBytes(value), PipeName);

    public IReadOnlyList<string> AuditLines() => new AuditLog(ProductRoot).ReadRecentLines(20);

    public async ValueTask DisposeAsync()
    {
        try { await AgentVaultClient.DeleteAsync(SessionAgentServiceNames.GhToken, PipeName); } catch { /* ignore */ }
        try { if (!_agent.HasExited) _agent.Kill(entireProcessTree: true); } catch { /* ignore */ }
        await _agent.WaitForExitAsync();
        _agent.Dispose();
        try { Directory.Delete(ProductRoot, recursive: true); } catch { /* ignore */ }
    }
}
