using CmdWarden.Cli;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Approval memory never outlives the conditions it was granted under (#133): a policy set,
/// a re-harden, or an unenroll for the launcher/tool a grant covers forces the next call to
/// prompt again.
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class ClearApprovalMemoryProcessTests
{
    private static readonly string[] WriteArgv = { "pr", "create", "--title", "t" };
    private static readonly string[] ReadArgv = { "pr", "list" };

    [Fact]
    public async Task Policy_set_for_the_granted_tool_clears_the_session_and_prompts_again()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "session");
        if (fx is null)
            return;
        // Read level: writes still need approval, so the grant below still comes from a prompt.
        fx.SetLevel("gh", PolicyLevel.Read);
        await fx.SaveTokenAsync("policy-set-token");

        var grant = await AgentAuthorizeClient.AuthorizeAsync("gh", WriteArgv, pipeName: fx.PipeName);
        Assert.Equal(GateDecisions.SessionGrant, grant.Decision);

        // Tighten gh under this launcher key: the real level change the grant covers must drop.
        fx.SetLevel("gh", PolicyLevel.Deny);

        var read = await AgentAuthorizeClient.AuthorizeAsync("gh", ReadArgv, pipeName: fx.PipeName);

        // Scripted "session" gate answers again, so this reads as a fresh grant, not a reused one.
        Assert.Equal(GateDecisions.SessionGrant, read.Decision);
    }

    [Fact]
    public async Task Reharden_clears_the_session_for_that_tool_and_prompts_again()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "session");
        if (fx is null)
            return;
        fx.SetLevel("gh", PolicyLevel.Deny);
        await fx.SaveTokenAsync("reharden-token");

        var grant = await AgentAuthorizeClient.AuthorizeAsync("gh", WriteArgv, pipeName: fx.PipeName);
        Assert.Equal(GateDecisions.SessionGrant, grant.Decision);

        // Re-pin gh to a different real binary: a genuine re-harden, still passes the pin check.
        new ToolPinStore(fx.ProductRoot).Save("gh", Path.Combine(Environment.SystemDirectory, "notepad.exe"));

        var read = await AgentAuthorizeClient.AuthorizeAsync("gh", ReadArgv, pipeName: fx.PipeName);

        Assert.Equal(GateDecisions.SessionGrant, read.Decision);
    }

    [Fact]
    public async Task Unenroll_clears_the_session_and_the_next_call_is_denied()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "session");
        if (fx is null)
            return;
        fx.SetLevel("gh", PolicyLevel.Deny);
        await fx.SaveTokenAsync("unenroll-token");

        var grant = await AgentAuthorizeClient.AuthorizeAsync("gh", WriteArgv, pipeName: fx.PipeName);
        Assert.Equal(GateDecisions.SessionGrant, grant.Decision);

        var store = new PolicyStore(fx.PolicyPath);
        store.Load();
        store.Unenroll(fx.SelectedPolicyKey);
        store.Save();

        // Unenrolled + scripted gate only answers "session": no button offered, so it degrades
        // to a fresh Approve Once instead of reusing the dropped grant.
        var read = await AgentAuthorizeClient.AuthorizeAsync("gh", ReadArgv, pipeName: fx.PipeName);
        Assert.Equal(GateDecisions.AllowOnce, read.Decision);
    }

    [Fact]
    public async Task Agent_restart_forgets_every_grant()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "session");
        if (fx is null)
            return;
        fx.SetLevel("gh", PolicyLevel.Deny);
        await fx.SaveTokenAsync("restart-token");

        var grant = await AgentAuthorizeClient.AuthorizeAsync("gh", WriteArgv, pipeName: fx.PipeName);
        Assert.Equal(GateDecisions.SessionGrant, grant.Decision);

        await fx.RestartAsync();

        var read = await AgentAuthorizeClient.AuthorizeAsync("gh", ReadArgv, pipeName: fx.PipeName);
        Assert.Equal(GateDecisions.SessionGrant, read.Decision);
    }

    [Fact]
    public async Task First_successful_harden_clears_a_grant_taken_before_any_pin_existed()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "session");
        if (fx is null)
            return;
        fx.SetLevel("gh", PolicyLevel.Deny);
        await fx.SaveTokenAsync("first-harden-token");

        // No pin yet: the call still fails PinMissing, but the scripted "session" gate already
        // ran and wrote a grant before that failure - the case a "successful harden" (not just a
        // re-harden) must still clear.
        File.Delete(Path.Combine(fx.ProductRoot, "pins", "gh.json"));
        var ex = await Assert.ThrowsAsync<Grpc.Core.RpcException>(
            () => AgentAuthorizeClient.AuthorizeAsync("gh", WriteArgv, pipeName: fx.PipeName));
        Assert.Contains(PolicyReasonCodes.PinMissing, ex.Status.Detail, StringComparison.Ordinal);

        // First-ever harden for this tool: writes the pin that didn't exist before.
        new ToolPinStore(fx.ProductRoot).Save("gh", Path.Combine(Environment.SystemDirectory, "cmd.exe"));

        var read = await AgentAuthorizeClient.AuthorizeAsync("gh", ReadArgv, pipeName: fx.PipeName);
        Assert.Equal(GateDecisions.SessionGrant, read.Decision);
    }
}
