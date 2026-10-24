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
}
