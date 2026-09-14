using CmdWarden.Agent.Approval;
using CmdWarden.Cli;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// "Allow for session" over the agent RPC surface with the scripted session gate (#132).
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class SessionAllowProcessTests
{
    private static readonly string[] WriteArgv = { "pr", "create", "--title", "t" };
    private static readonly string[] ReadArgv = { "pr", "list" };

    [Fact]
    public async Task Grant_returns_session_grant_and_lower_class_call_returns_session_allow()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "session");
        if (fx is null)
            return;
        // Deny level: even reads need approval, so the read below can only pass via the session.
        fx.SetLevel("gh", PolicyLevel.Deny);
        var token = "session-" + Guid.NewGuid().ToString("N");
        await fx.SaveTokenAsync(token);

        var grant = await AgentAuthorizeClient.AuthorizeAsync("gh", WriteArgv, pipeName: fx.PipeName);
        var read = await AgentAuthorizeClient.AuthorizeAsync("gh", ReadArgv, pipeName: fx.PipeName);

        Assert.True(grant.Allowed);
        Assert.Equal(GateDecisions.SessionGrant, grant.Decision);
        Assert.Equal("", grant.ReasonCode);
        Assert.True(read.Allowed);
        Assert.Equal(GateDecisions.SessionAllow, read.Decision);
        Assert.Equal(PolicyReasonCodes.SessionAllow, read.ReasonCode);
        Assert.Equal(token, read.Env[SessionAgentServiceNames.GhToken]);

        var lines = fx.AuditLines();
        Assert.Single(lines, l => l.Contains("\"decision\":\"session-grant\"", StringComparison.Ordinal));
        Assert.Single(lines, l => l.Contains("\"decision\":\"session-allow\"", StringComparison.Ordinal)
            && l.Contains(PolicyReasonCodes.SessionAllow, StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Secret_reveal_still_prompts_under_an_active_session()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "session");
        if (fx is null)
            return;
        var token = "reveal-" + Guid.NewGuid().ToString("N");
        await fx.SaveTokenAsync(token);

        var grant = await AgentAuthorizeClient.AuthorizeAsync("gh", WriteArgv, pipeName: fx.PipeName);
        Assert.Equal(GateDecisions.SessionGrant, grant.Decision);

        // Scripted gate answers "session" again, which degrades to Approve Once for secret-reveal.
        var reveal = await AgentAuthorizeClient.AuthorizeAsync("gh", new[] { "auth", "token" }, pipeName: fx.PipeName);

        Assert.Equal(GateDecisions.AllowOnce, reveal.Decision);
        Assert.Equal("", reveal.ReasonCode);
        Assert.DoesNotContain(fx.AuditLines(), l => l.Contains(PolicyReasonCodes.SessionAllow, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Idle_expiry_prompts_again()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "session",
            env: new Dictionary<string, string> { [ApprovalMemory.SessionIdleEnvVar] = "1" });
        if (fx is null)
            return;
        fx.SetLevel("gh", PolicyLevel.Deny);
        await fx.SaveTokenAsync("idle-token");

        _ = await AgentAuthorizeClient.AuthorizeAsync("gh", WriteArgv, pipeName: fx.PipeName);
        await Task.Delay(TimeSpan.FromSeconds(3));
        var read = await AgentAuthorizeClient.AuthorizeAsync("gh", ReadArgv, pipeName: fx.PipeName);

        // Prompted again: the scripted gate hands out a fresh grant instead of reusing the idle one.
        Assert.Equal(GateDecisions.SessionGrant, read.Decision);
        Assert.DoesNotContain(fx.AuditLines(), l => l.Contains(PolicyReasonCodes.SessionAllow, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unenrolled_launcher_is_never_granted_a_session()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "session", enroll: null);
        if (fx is null)
            return;
        await fx.SaveTokenAsync("unenrolled-token");

        var first = await AgentAuthorizeClient.AuthorizeAsync("gh", WriteArgv, pipeName: fx.PipeName);
        var second = await AgentAuthorizeClient.AuthorizeAsync("gh", ReadArgv, pipeName: fx.PipeName);

        Assert.Equal(GateDecisions.AllowOnce, first.Decision);
        Assert.Equal(GateDecisions.AllowOnce, second.Decision);
        var lines = fx.AuditLines();
        Assert.DoesNotContain(lines, l => l.Contains("session-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReleaseSecret_is_covered_by_a_session_granted_on_authorize()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "session");
        if (fx is null)
            return;
        fx.SetLevel("gh", PolicyLevel.Deny);
        var token = "release-session-" + Guid.NewGuid().ToString("N");
        await fx.SaveTokenAsync(token);

        _ = await AgentAuthorizeClient.AuthorizeAsync("gh", WriteArgv, pipeName: fx.PipeName);
        var release = await AgentVaultClient.ReleaseAsync(
            SessionAgentServiceNames.GhToken, pipeName: fx.PipeName, tool: "gh", commandClass: "read");

        Assert.Equal(GateDecisions.SessionAllow, release.Decision);
        Assert.Equal(token, release.Value.ToStringUtf8());
        var lines = fx.AuditLines();
        Assert.Single(lines, l => l.Contains("\"decision\":\"session-allow\"", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReleaseSecret_grant_click_is_its_own_audit_row()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "session");
        if (fx is null)
            return;
        fx.SetLevel("gh", PolicyLevel.Deny);
        var token = "release-grant-" + Guid.NewGuid().ToString("N");
        await fx.SaveTokenAsync(token);

        var grant = await AgentVaultClient.ReleaseAsync(
            SessionAgentServiceNames.GhToken, pipeName: fx.PipeName, tool: "gh", commandClass: "write");

        Assert.Equal(GateDecisions.SessionGrant, grant.Decision);
        var lines = fx.AuditLines();
        Assert.Single(lines, l => l.Contains("\"decision\":\"session-grant\"", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Deny_and_approve_once_are_unchanged()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "allow");
        if (fx is null)
            return;
        await fx.SaveTokenAsync("plain-token");

        var grant = await AgentAuthorizeClient.AuthorizeAsync("gh", WriteArgv, pipeName: fx.PipeName);

        Assert.Equal(GateDecisions.AllowOnce, grant.Decision);
        Assert.DoesNotContain(fx.AuditLines(), l => l.Contains("session-", StringComparison.Ordinal));
    }
}
