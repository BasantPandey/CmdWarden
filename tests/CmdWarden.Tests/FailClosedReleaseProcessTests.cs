using System.Text;
using Grpc.Core;
using CmdWarden.Cli;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Fail-closed ReleaseSecret (#199): the audit row persists before any value returns.
/// A read-only audit directory blocks every grant path and still records deny best-effort.
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class FailClosedReleaseProcessTests
{
    private static readonly string[] DockerPull = { "pull", "alpine" };

    [Fact]
    public async Task Auto_allow_release_writes_a_gate_row_with_the_secret_name()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "off", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        var name = "cw_fc_" + Guid.NewGuid().ToString("N")[..10];
        var value = "value-" + Guid.NewGuid().ToString("N");
        await AgentVaultClient.SaveAsync(name, Encoding.UTF8.GetBytes(value), fx.PipeName);
        try
        {
            var released = await AgentVaultClient.ReleaseAsync(name, "inject", fx.PipeName);
            Assert.Equal(GateDecisions.AutoAllow, released.Decision);

            var lines = fx.AuditLines();
            var row = Assert.Single(lines, l => l.Contains("\"tool\":\"inject\"", StringComparison.Ordinal));
            Assert.Contains("\"decision\":\"auto-allow\"", row, StringComparison.Ordinal);
            Assert.Contains($"\"secretName\":\"{name}\"", row, StringComparison.Ordinal);
            Assert.Contains("\"purpose\":\"inject\"", row, StringComparison.Ordinal);
            Assert.DoesNotContain(value, row, StringComparison.Ordinal);
        }
        finally
        {
            try { await AgentVaultClient.DeleteAsync(name, fx.PipeName); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task First_time_allow_once_writes_a_row_before_the_value_returns()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "allow");
        if (fx is null)
            return;
        var name = "cw_fc_" + Guid.NewGuid().ToString("N")[..10];
        await AgentVaultClient.SaveAsync(name, Encoding.UTF8.GetBytes("v"), fx.PipeName);
        try
        {
            var released = await AgentVaultClient.ReleaseAsync(name, "inject", fx.PipeName);
            Assert.Equal(GateDecisions.AllowOnce, released.Decision);

            var row = Assert.Single(fx.AuditLines(), l => l.Contains("\"tool\":\"inject\"", StringComparison.Ordinal));
            Assert.Contains("\"decision\":\"allow-once\"", row, StringComparison.Ordinal);
            Assert.Contains($"\"secretName\":\"{name}\"", row, StringComparison.Ordinal);
        }
        finally
        {
            try { await AgentVaultClient.DeleteAsync(name, fx.PipeName); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task First_time_deny_writes_a_row()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "deny");
        if (fx is null)
            return;
        var name = "cw_fc_" + Guid.NewGuid().ToString("N")[..10];
        await AgentVaultClient.SaveAsync(name, Encoding.UTF8.GetBytes("v"), fx.PipeName);
        try
        {
            var ex = await Assert.ThrowsAsync<RpcException>(() =>
                AgentVaultClient.ReleaseAsync(name, "inject", fx.PipeName));
            Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);

            var row = Assert.Single(fx.AuditLines(), l => l.Contains("\"tool\":\"inject\"", StringComparison.Ordinal));
            Assert.Contains("\"decision\":\"deny\"", row, StringComparison.Ordinal);
            Assert.Contains(PolicyReasonCodes.UserDenied, row, StringComparison.Ordinal);
        }
        finally
        {
            try { await AgentVaultClient.DeleteAsync(name, fx.PipeName); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Unavailable_gate_writes_a_row()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;
        var name = "cw_fc_" + Guid.NewGuid().ToString("N")[..10];
        await AgentVaultClient.SaveAsync(name, Encoding.UTF8.GetBytes("v"), fx.PipeName);
        try
        {
            var ex = await Assert.ThrowsAsync<RpcException>(() =>
                AgentVaultClient.ReleaseAsync(name, "inject", fx.PipeName));
            Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);

            var row = Assert.Single(fx.AuditLines(), l => l.Contains("\"tool\":\"inject\"", StringComparison.Ordinal));
            Assert.Contains("\"decision\":\"unavailable\"", row, StringComparison.Ordinal);
        }
        finally
        {
            try { await AgentVaultClient.DeleteAsync(name, fx.PipeName); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Read_only_audit_dir_blocks_auto_allow_release_with_no_value()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "off", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        var name = "cw_fc_" + Guid.NewGuid().ToString("N")[..10];
        await AgentVaultClient.SaveAsync(name, Encoding.UTF8.GetBytes("v"), fx.PipeName);
        using var _ = new ReadOnlyDir(Path.Combine(fx.ProductRoot, "audit"));
        try
        {
            var ex = await Assert.ThrowsAsync<RpcException>(() =>
                AgentVaultClient.ReleaseAsync(name, "inject", fx.PipeName));
            Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
            Assert.Contains(
                $"{PolicyReasonCodes.ApprovalUnavailable}: audit write failed; release blocked: ",
                ex.Status.Detail, StringComparison.Ordinal);
            Assert.Empty(fx.AuditLines());
        }
        finally
        {
            try { await AgentVaultClient.DeleteAsync(name, fx.PipeName); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Read_only_audit_dir_blocks_first_time_allow_once_release()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "allow");
        if (fx is null)
            return;
        var name = "cw_fc_" + Guid.NewGuid().ToString("N")[..10];
        await AgentVaultClient.SaveAsync(name, Encoding.UTF8.GetBytes("v"), fx.PipeName);
        using var _ = new ReadOnlyDir(Path.Combine(fx.ProductRoot, "audit"));
        try
        {
            var ex = await Assert.ThrowsAsync<RpcException>(() =>
                AgentVaultClient.ReleaseAsync(name, "inject", fx.PipeName));
            Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
            Assert.Contains(PolicyReasonCodes.ApprovalUnavailable, ex.Status.Detail, StringComparison.Ordinal);
        }
        finally
        {
            try { await AgentVaultClient.DeleteAsync(name, fx.PipeName); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Read_only_audit_dir_still_returns_PermissionDenied_on_deny()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "deny");
        if (fx is null)
            return;
        var name = "cw_fc_" + Guid.NewGuid().ToString("N")[..10];
        await AgentVaultClient.SaveAsync(name, Encoding.UTF8.GetBytes("v"), fx.PipeName);
        using var _ = new ReadOnlyDir(Path.Combine(fx.ProductRoot, "audit"));
        try
        {
            var ex = await Assert.ThrowsAsync<RpcException>(() =>
                AgentVaultClient.ReleaseAsync(name, "inject", fx.PipeName));
            Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
            Assert.Contains(PolicyReasonCodes.UserDenied, ex.Status.Detail, StringComparison.Ordinal);
        }
        finally
        {
            try { await AgentVaultClient.DeleteAsync(name, fx.PipeName); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Read_only_audit_dir_blocks_docker_authorize_before_any_env()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "off", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        new ToolPinStore(fx.ProductRoot).Save("docker", Path.Combine(Environment.SystemDirectory, "cmd.exe"));
        using var _ = new ReadOnlyDir(Path.Combine(fx.ProductRoot, "audit"));

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            AgentAuthorizeClient.AuthorizeAsync("docker", DockerPull, pipeName: fx.PipeName));
        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
        Assert.Contains(PolicyReasonCodes.ApprovalUnavailable, ex.Status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inject_cli_prints_the_blocked_line_and_exits_1()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "off", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        var name = "cw_fc_" + Guid.NewGuid().ToString("N")[..10];
        await AgentVaultClient.SaveAsync(name, Encoding.UTF8.GetBytes("v"), fx.PipeName);
        var auditDir = Path.Combine(fx.ProductRoot, "audit");
        using var _ = new ReadOnlyDir(auditDir);

        var previousPipe = Environment.GetEnvironmentVariable("CW_PIPE_NAME");
        var previousRoot = Environment.GetEnvironmentVariable(ProductPaths.EnvVar);
        var originalErr = Console.Error;
        var err = new StringWriter();
        Environment.SetEnvironmentVariable("CW_PIPE_NAME", fx.PipeName);
        Environment.SetEnvironmentVariable(ProductPaths.EnvVar, fx.ProductRoot);
        Console.SetError(err);
        try
        {
            var exit = await CliApp.RunAsync(new[] { "inject", "+" + name, "--", "cmd.exe", "/c", "exit 0" });
            Assert.Equal(1, exit);
            Assert.Contains(
                $"Release blocked: audit log not writable ({auditDir}). Fix the audit directory and retry.",
                err.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalErr);
            Environment.SetEnvironmentVariable("CW_PIPE_NAME", previousPipe);
            Environment.SetEnvironmentVariable(ProductPaths.EnvVar, previousRoot);
            try { await AgentVaultClient.DeleteAsync(name, fx.PipeName); } catch { /* ignore */ }
        }
    }
}
