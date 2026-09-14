using CmdWarden.Cli;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// `cw policy sessions` list + revoke over the new RPCs against a real agent process (#134).
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class PolicySessionsProcessTests
{
    private static readonly string[] WriteArgv = { "pr", "create", "--title", "t" };

    [Fact]
    public async Task List_shows_the_grant_then_revoke_removes_it_and_the_next_call_prompts()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "session");
        if (fx is null)
            return;
        fx.SetLevel("gh", PolicyLevel.Deny);
        var token = "sessions-" + Guid.NewGuid().ToString("N");
        await fx.SaveTokenAsync(token);

        var grant = await AgentAuthorizeClient.AuthorizeAsync("gh", WriteArgv, pipeName: fx.PipeName);
        Assert.Equal(GateDecisions.SessionGrant, grant.Decision);

        var rows = await AgentSessionsClient.ListAsync(fx.PipeName);
        var row = Assert.Single(rows);
        Assert.False(string.IsNullOrWhiteSpace(row.Id));
        Assert.Equal("gh", row.Tool);
        Assert.Equal(SessionAgentServiceNames.GhToken, row.SecretName);
        Assert.Equal(CommandClassNames.Write, row.CommandClass);
        Assert.True(row.Pid > 0);
        Assert.Equal(fx.SelectedPolicyKey, row.LauncherPolicyKey);
        // Names only: the token value never rides the list RPC.
        Assert.DoesNotContain(token, string.Join('\n', rows.Select(r => r.ToString())), StringComparison.Ordinal);

        var removed = await AgentSessionsClient.RevokeAsync(row.Id, all: false, pipeName: fx.PipeName);
        Assert.Equal(1, removed);
        Assert.Empty(await AgentSessionsClient.ListAsync(fx.PipeName));

        // Grant gone: the scripted gate hands out a fresh one instead of a silent reuse.
        var again = await AgentAuthorizeClient.AuthorizeAsync("gh", new[] { "pr", "list" }, pipeName: fx.PipeName);
        Assert.Equal(GateDecisions.SessionGrant, again.Decision);
        Assert.NotEqual(PolicyReasonCodes.SessionAllow, again.ReasonCode);
    }

    [Fact]
    public async Task Revoke_all_returns_the_count_removed()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "session");
        if (fx is null)
            return;
        fx.SetLevel("gh", PolicyLevel.Deny);
        await fx.SaveTokenAsync("revoke-all-token");

        _ = await AgentAuthorizeClient.AuthorizeAsync("gh", WriteArgv, pipeName: fx.PipeName);
        Assert.Single(await AgentSessionsClient.ListAsync(fx.PipeName));

        var removed = await AgentSessionsClient.RevokeAsync(id: null, all: true, pipeName: fx.PipeName);
        Assert.Equal(1, removed);
        Assert.Empty(await AgentSessionsClient.ListAsync(fx.PipeName));
    }

    [Fact]
    public async Task Empty_list_over_the_rpc_when_no_grant_exists()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "session");
        if (fx is null)
            return;

        Assert.Empty(await AgentSessionsClient.ListAsync(fx.PipeName));
        Assert.Equal(0, await AgentSessionsClient.RevokeAsync("missing", all: false, pipeName: fx.PipeName));
    }

    [Fact]
    public async Task Cli_lists_the_grant_and_revoke_clears_it()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "session");
        if (fx is null)
            return;
        fx.SetLevel("gh", PolicyLevel.Deny);
        var token = "cli-sessions-" + Guid.NewGuid().ToString("N");
        await fx.SaveTokenAsync(token);
        _ = await AgentAuthorizeClient.AuthorizeAsync("gh", WriteArgv, pipeName: fx.PipeName);
        var id = (await AgentSessionsClient.ListAsync(fx.PipeName)).Single().Id;

        var previousPipe = Environment.GetEnvironmentVariable("CW_PIPE_NAME");
        Environment.SetEnvironmentVariable("CW_PIPE_NAME", fx.PipeName);
        var originalOut = Console.Out;
        try
        {
            var listed = await CaptureAsync(() => CliApp.RunAsync(new[] { "policy", "sessions" }));
            Assert.Equal(0, listed.ExitCode);
            Assert.Contains(id, listed.Output, StringComparison.Ordinal);
            Assert.Contains("gh / " + SessionAgentServiceNames.GhToken, listed.Output, StringComparison.Ordinal);
            Assert.DoesNotContain(token, listed.Output, StringComparison.Ordinal);

            var revoked = await CaptureAsync(() => CliApp.RunAsync(new[] { "policy", "sessions", "--revoke", id }));
            Assert.Equal(0, revoked.ExitCode);
            Assert.Contains("Revoked session allow " + id, revoked.Output, StringComparison.Ordinal);

            var empty = await CaptureAsync(() => CliApp.RunAsync(new[] { "policy", "sessions" }));
            Assert.Equal(0, empty.ExitCode);
            Assert.Contains("No active session allows", empty.Output, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOut);
            Environment.SetEnvironmentVariable("CW_PIPE_NAME", previousPipe);
        }
    }

    private static async Task<(int ExitCode, string Output)> CaptureAsync(Func<Task<int>> run)
    {
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            var exit = await run();
            return (exit, writer.ToString());
        }
        finally
        {
            Console.Out.Flush();
        }
    }
}
