using System.Diagnostics;
using CmdWarden.Cli;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Process-level named-pipe gRPC health (issue #2).
/// Spawns the Session Agent executable so we do not reference the Web SDK project from tests.
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class SessionAgentHealthTests
{
    [Fact]
    public async Task GetHealth_reports_alive_when_agent_process_running()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var pipeName = $"{AgentEndpoints.PipeNamePrefix}-test-{Guid.NewGuid():N}";
        await using var agent = await AgentProcess.StartAsync(TestPaths.FindAgentDll(), pipeName);

        var health = await AgentHealthClient.GetHealthAsync(pipeName, TimeSpan.FromSeconds(10));

        Assert.True(health.Alive);
        Assert.Equal(ProductInfo.Name, health.Product);
        Assert.Equal(ProductInfo.Version, health.Version);
        Assert.Equal(pipeName, health.PipeName);
        Assert.True(health.ProcessId > 0);
        Assert.False(string.IsNullOrWhiteSpace(health.UserName));
    }

    [Fact]
    public async Task GetHealth_fails_when_agent_down()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var missing = $"{AgentEndpoints.PipeNamePrefix}-missing-{Guid.NewGuid():N}";
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => AgentHealthClient.GetHealthAsync(missing, TimeSpan.FromMilliseconds(800)));

        Assert.True(AgentHealthClient.IsAgentUnreachable(ex));
    }

    private sealed class AgentProcess : IAsyncDisposable
    {
        private readonly Process _process;

        private AgentProcess(Process process) => _process = process;

        public static async Task<AgentProcess> StartAsync(string agentDll, string pipeName)
        {
            // Pass pipe name via env so Program can override without changing public API much.
            // Prefer: re-run with args — AgentHost.Build(args, pipeName) uses args for host only.
            // We'll set CW_PIPE_NAME env and read it in Program.
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
            // Keep the policy and audit files of the test out of the real product root.
            var productRoot = Path.Combine(Path.GetTempPath(), "cw-prod-" + Guid.NewGuid().ToString("N"));
            psi.Environment[ProductPaths.EnvVar] = productRoot;
            psi.Environment["CW_POLICY_PATH"] = Path.Combine(productRoot, "policy.json");
            // Never show Approval Gate UI in automated tests.
            psi.Environment["CW_APPROVAL_MODE"] = "off";

            var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start Session Agent process.");

            // Wait until agent is healthy or timeout
            var deadline = DateTime.UtcNow.AddSeconds(25);
            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    var err = await process.StandardError.ReadToEndAsync();
                    var stdout = await process.StandardOutput.ReadToEndAsync();
                    throw new InvalidOperationException(
                        $"Agent exited early ({process.ExitCode}). stdout={stdout} stderr={err}");
                }

                try
                {
                    _ = await AgentHealthClient.GetHealthAsync(pipeName, TimeSpan.FromMilliseconds(400));
                    return new AgentProcess(process);
                }
                catch
                {
                    await Task.Delay(150);
                }
            }

            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }

            throw new TimeoutException("Session Agent did not become healthy in time.");
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }

            await _process.WaitForExitAsync().ConfigureAwait(false);
            _process.Dispose();
        }
    }
}
