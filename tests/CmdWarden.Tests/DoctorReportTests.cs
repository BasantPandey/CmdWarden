using System.Diagnostics;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Doctor tab data (issue #115): Contracts-only gather, no lazy start.
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class DoctorReportTests
{
    [Fact]
    public async Task Gather_reports_down_without_starting_agent()
    {
        var pipe = $"{AgentEndpoints.PipeNamePrefix}-doctor-missing-{Guid.NewGuid():N}";

        var report = await DoctorReport.GatherAsync(pipe, TimeSpan.FromMilliseconds(800));

        Assert.False(report.AgentUp);
        Assert.Null(report.Health);
        Assert.False(report.VersionMismatch);
        Assert.False(string.IsNullOrWhiteSpace(report.AgentDetail));
        Assert.Equal(pipe, report.ExpectedPipe);
        Assert.Equal(ProductInfo.Name, report.ProductName);
        Assert.Equal(ProductInfo.Version, report.ProductVersion);
        Assert.False(string.IsNullOrWhiteSpace(report.ProductRoot));
        Assert.EndsWith(SecretsManagerStartMenu.ShortcutFileName, report.ShortcutPath);

        // Still down afterwards: gather must never start the agent.
        var again = await AgentLifecycle.StatusAsync(pipe, TimeSpan.FromMilliseconds(500));
        Assert.False(again.Up);
    }

    [Fact]
    public async Task Gather_reports_up_with_health_when_agent_running()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var pipe = $"{AgentEndpoints.PipeNamePrefix}-doctor-{Guid.NewGuid():N}";
        await using var agent = await StartAgentAsync(pipe);

        var report = await DoctorReport.GatherAsync(pipe, TimeSpan.FromSeconds(10));

        Assert.True(report.AgentUp);
        Assert.NotNull(report.Health);
        Assert.Equal(pipe, report.Health!.PipeName);
        Assert.True(report.Health.ProcessId > 0);
        Assert.False(report.VersionMismatch);
    }

    [Fact]
    public void VersionMismatch_flags_different_agent_version()
    {
        var health = new CmdWarden.Contracts.Grpc.HealthResponse { Version = "9.9.9" };
        var report = new DoctorReport(
            "CmdWarden", "0.1.0", "root", "pipe", null, null, "x.lnk", false, true, null, health);

        Assert.True(report.VersionMismatch);
    }

    private static async Task<AgentHandle> StartAgentAsync(string pipeName)
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
        psi.Environment["CW_APPROVAL_MODE"] = "off";
        var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start agent.");

        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
                throw new InvalidOperationException($"Agent exited early ({process.ExitCode}).");
            try
            {
                _ = await AgentHealthClient.GetHealthAsync(pipeName, TimeSpan.FromMilliseconds(400));
                return new AgentHandle(process);
            }
            catch
            {
                await Task.Delay(150);
            }
        }

        try { process.Kill(entireProcessTree: true); } catch { }
        throw new TimeoutException("Session Agent did not become healthy in time.");
    }

    private sealed class AgentHandle(Process process) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            process.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
