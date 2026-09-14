using CmdWarden.Agent.Approval;
using CmdWarden.Cli;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Transient reuse of a human Approval Gate decision (#131): exact retry from the same process
/// inside the window reuses the outcome; audited with reason TransientReuse.
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class TransientReuseProcessTests
{
    private static readonly string[] WriteArgv = { "pr", "create", "--title", "t" };

    [Fact]
    public async Task Authorize_reuses_human_allow_for_exact_retry()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "allow");
        if (fx is null)
            return;
        var token = "reuse-allow-" + Guid.NewGuid().ToString("N");
        await fx.SaveTokenAsync(token);

        var first = await AgentAuthorizeClient.AuthorizeAsync("gh", WriteArgv, pipeName: fx.PipeName);
        var second = await AgentAuthorizeClient.AuthorizeAsync("gh", WriteArgv, pipeName: fx.PipeName);

        Assert.Equal("allow-once", first.Decision);
        Assert.Equal("", first.ReasonCode);
        Assert.Equal("allow-once", second.Decision);
        Assert.Equal(PolicyReasonCodes.TransientReuse, second.ReasonCode);
        Assert.Equal(token, second.Env[SessionAgentServiceNames.GhToken]);

        var lines = fx.AuditLines();
        Assert.Single(lines, l => l.Contains(PolicyReasonCodes.TransientReuse, StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Authorize_reuses_human_deny_for_exact_retry()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "deny");
        if (fx is null)
            return;
        var token = "reuse-deny-" + Guid.NewGuid().ToString("N");
        await fx.SaveTokenAsync(token);

        var first = await Assert.ThrowsAsync<Grpc.Core.RpcException>(() =>
            AgentAuthorizeClient.AuthorizeAsync("gh", WriteArgv, pipeName: fx.PipeName));
        var second = await Assert.ThrowsAsync<Grpc.Core.RpcException>(() =>
            AgentAuthorizeClient.AuthorizeAsync("gh", WriteArgv, pipeName: fx.PipeName));

        Assert.Equal(Grpc.Core.StatusCode.PermissionDenied, first.StatusCode);
        Assert.Equal(Grpc.Core.StatusCode.PermissionDenied, second.StatusCode);
        Assert.Contains(PolicyReasonCodes.UserDenied, second.Status.Detail, StringComparison.Ordinal);
        Assert.Contains(PolicyReasonCodes.TransientReuse, second.Status.Detail, StringComparison.Ordinal);

        var lines = fx.AuditLines();
        Assert.Single(lines, l => l.Contains(PolicyReasonCodes.UserDenied, StringComparison.Ordinal));
        Assert.Single(lines, l => l.Contains(PolicyReasonCodes.TransientReuse, StringComparison.Ordinal)
            && l.Contains("\"decision\":\"deny\"", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Authorize_prompts_again_when_command_line_changes()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "allow");
        if (fx is null)
            return;
        await fx.SaveTokenAsync("miss-token");

        _ = await AgentAuthorizeClient.AuthorizeAsync("gh", WriteArgv, pipeName: fx.PipeName);
        var second = await AgentAuthorizeClient.AuthorizeAsync(
            "gh", new[] { "pr", "create", "--title", "other" }, pipeName: fx.PipeName);

        Assert.Equal("allow-once", second.Decision);
        Assert.Equal("", second.ReasonCode);
        Assert.DoesNotContain(fx.AuditLines(), l => l.Contains(PolicyReasonCodes.TransientReuse, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Authorize_prompts_again_after_window_expires()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "allow",
            env: new Dictionary<string, string> { [ApprovalMemory.TransientWindowEnvVar] = "1" });
        if (fx is null)
            return;
        await fx.SaveTokenAsync("expiry-token");

        _ = await AgentAuthorizeClient.AuthorizeAsync("gh", WriteArgv, pipeName: fx.PipeName);
        await Task.Delay(TimeSpan.FromSeconds(3));
        var second = await AgentAuthorizeClient.AuthorizeAsync("gh", WriteArgv, pipeName: fx.PipeName);

        Assert.Equal("allow-once", second.Decision);
        Assert.Equal("", second.ReasonCode);
        Assert.DoesNotContain(fx.AuditLines(), l => l.Contains(PolicyReasonCodes.TransientReuse, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReleaseSecret_reuses_human_allow_for_exact_retry()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "allow");
        if (fx is null)
            return;
        var token = "reuse-release-" + Guid.NewGuid().ToString("N");
        await fx.SaveTokenAsync(token);

        // AiHarness default Read: inject/write needs approval.
        var first = await AgentVaultClient.ReleaseAsync(SessionAgentServiceNames.GhToken, pipeName: fx.PipeName);
        var second = await AgentVaultClient.ReleaseAsync(SessionAgentServiceNames.GhToken, pipeName: fx.PipeName);

        Assert.Equal("allow-once", first.Decision);
        Assert.Equal("allow-once", second.Decision);
        Assert.Equal(token, second.Value.ToStringUtf8());

        var lines = fx.AuditLines();
        Assert.Single(lines, l => l.Contains(PolicyReasonCodes.TransientReuse, StringComparison.Ordinal)
            && l.Contains("\"decision\":\"allow-once\"", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReleaseSecret_reuse_keys_on_command_line()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "allow");
        if (fx is null)
            return;
        await fx.SaveTokenAsync("reuse-cmdline-" + Guid.NewGuid().ToString("N"));

        var name = SessionAgentServiceNames.GhToken;
        _ = await AgentVaultClient.ReleaseAsync(name, pipeName: fx.PipeName, commandLine: "cmd /c echo a");
        _ = await AgentVaultClient.ReleaseAsync(name, pipeName: fx.PipeName, commandLine: "cmd /c echo a");
        _ = await AgentVaultClient.ReleaseAsync(name, pipeName: fx.PipeName, commandLine: "cmd /c echo b");

        var lines = fx.AuditLines();
        Assert.Single(lines, l => l.Contains(PolicyReasonCodes.TransientReuse, StringComparison.Ordinal));
        // The audit row gains no new field (#200).
        Assert.DoesNotContain(lines, l => l.Contains("echo a", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReleaseSecret_card_payload_shows_command_line_and_secret_name()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var capture = Path.Combine(Path.GetTempPath(), "cw-card-" + Guid.NewGuid().ToString("N") + ".json");
        var helper = Path.Combine(Path.GetTempPath(), "cw-fake-gate-" + Guid.NewGuid().ToString("N") + ".cmd");
        await File.WriteAllTextAsync(helper, $"""
            @echo off
            copy /y %2 "{capture}" >nul
            exit /b {ApprovalHelperExitCodes.AllowOnce}
            """);
        try
        {
            await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "prompt",
                env: new Dictionary<string, string> { [ProcessApprovalGate.HelperPathEnvVar] = helper });
            if (fx is null)
                return;
            await fx.SaveTokenAsync("card-" + Guid.NewGuid().ToString("N"));

            var release = await AgentVaultClient.ReleaseAsync(
                SessionAgentServiceNames.GhToken, pipeName: fx.PipeName, commandLine: "cmd /c echo card");

            Assert.Equal("allow-once", release.Decision);
            var payload = ApprovalHelperJson.TryDeserialize(await File.ReadAllTextAsync(capture));
            Assert.NotNull(payload);
            Assert.Equal("cmd /c echo card", payload!.CommandLine);
            Assert.Equal(new[] { SessionAgentServiceNames.GhToken }, payload.SecretNames);
        }
        finally
        {
            try { File.Delete(helper); } catch { /* ignore */ }
            try { File.Delete(capture); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task ReleaseSecret_reuses_human_deny_for_exact_retry()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "deny");
        if (fx is null)
            return;
        var token = "reuse-release-deny-" + Guid.NewGuid().ToString("N");
        await fx.SaveTokenAsync(token);

        _ = await Assert.ThrowsAsync<Grpc.Core.RpcException>(() =>
            AgentVaultClient.ReleaseAsync(SessionAgentServiceNames.GhToken, pipeName: fx.PipeName));
        var second = await Assert.ThrowsAsync<Grpc.Core.RpcException>(() =>
            AgentVaultClient.ReleaseAsync(SessionAgentServiceNames.GhToken, pipeName: fx.PipeName));

        Assert.Equal(Grpc.Core.StatusCode.PermissionDenied, second.StatusCode);
        Assert.Contains(PolicyReasonCodes.TransientReuse, second.Status.Detail, StringComparison.Ordinal);

        var lines = fx.AuditLines();
        Assert.Single(lines, l => l.Contains(PolicyReasonCodes.TransientReuse, StringComparison.Ordinal)
            && l.Contains("\"decision\":\"deny\"", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains(token, StringComparison.Ordinal));
    }
}
