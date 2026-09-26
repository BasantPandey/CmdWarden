using CmdWarden.Cli;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// One "Allow for session" click must hold for the whole launcher session. The card counts
/// every time it shows, so a second card is the repeat the session grant exists to stop.
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class SessionAllowCoverageProcessTests
{
    [Fact]
    public async Task One_session_click_covers_a_later_unknown_command()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var gate = CountingGate.AllowForSession();
        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "prompt",
            env: gate.AgentEnv);
        if (fx is null)
            return;
        await fx.SaveTokenAsync("coverage-" + Guid.NewGuid().ToString("N"));

        // A real harness session: a write the user allows for the session, then more work.
        var write = await AgentAuthorizeClient.AuthorizeAsync(
            "gh", new[] { "pr", "create", "--title", "t" }, pipeName: fx.PipeName);
        var sameClass = await AgentAuthorizeClient.AuthorizeAsync(
            "gh", new[] { "pr", "merge", "5" }, pipeName: fx.PipeName);
        var unknown = await AgentAuthorizeClient.AuthorizeAsync(
            "gh", new[] { "api", "repos/o/r" }, pipeName: fx.PipeName);

        Assert.Equal(GateDecisions.SessionGrant, write.Decision);
        Assert.Equal(GateDecisions.SessionAllow, sameClass.Decision);
        Assert.Equal(GateDecisions.SessionAllow, unknown.Decision);
        Assert.Equal(1, gate.CardCount);
    }

    [Fact]
    public async Task Secret_reveal_still_shows_its_own_card()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var gate = CountingGate.AllowForSession();
        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "prompt",
            env: gate.AgentEnv);
        if (fx is null)
            return;
        await fx.SaveTokenAsync("reveal-" + Guid.NewGuid().ToString("N"));

        _ = await AgentAuthorizeClient.AuthorizeAsync(
            "gh", new[] { "pr", "create", "--title", "t" }, pipeName: fx.PipeName);
        var reveal = await AgentAuthorizeClient.AuthorizeAsync(
            "gh", new[] { "auth", "token" }, pipeName: fx.PipeName);

        Assert.Equal(GateDecisions.AllowOnce, reveal.Decision);
        Assert.Equal(2, gate.CardCount);
    }

    [Fact]
    public async Task Session_grant_at_read_still_cards_once_for_a_write()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var gate = CountingGate.AllowForSession();
        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "prompt",
            env: gate.AgentEnv);
        if (fx is null)
            return;
        fx.SetLevel("gh", PolicyLevel.Deny);
        await fx.SaveTokenAsync("readfirst-" + Guid.NewGuid().ToString("N"));

        var read = await AgentAuthorizeClient.AuthorizeAsync(
            "gh", new[] { "pr", "list" }, pipeName: fx.PipeName);
        var write = await AgentAuthorizeClient.AuthorizeAsync(
            "gh", new[] { "pr", "create", "--title", "t" }, pipeName: fx.PipeName);

        // Read to write is more power, so the card comes back once. The read grant survives it.
        Assert.Equal(GateDecisions.SessionGrant, read.Decision);
        Assert.Equal(GateDecisions.SessionGrant, write.Decision);
        Assert.Equal(2, gate.CardCount);

        var again = await AgentAuthorizeClient.AuthorizeAsync(
            "gh", new[] { "pr", "list" }, pipeName: fx.PipeName);
        Assert.True(again.Allowed);
        Assert.Equal(2, gate.CardCount);
    }

    [Fact]
    public async Task Approve_once_lasts_the_session_for_the_same_class()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var gate = CountingGate.AllowOnce();
        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "prompt",
            env: gate.AgentEnv);
        if (fx is null)
            return;
        await fx.SaveTokenAsync("once-" + Guid.NewGuid().ToString("N"));

        var first = await AgentAuthorizeClient.AuthorizeAsync(
            "gh", new[] { "pr", "create", "--title", "a" }, pipeName: fx.PipeName);
        var newCommand = await AgentAuthorizeClient.AuthorizeAsync(
            "gh", new[] { "pr", "create", "--title", "b" }, pipeName: fx.PipeName);
        var sameClass = await AgentAuthorizeClient.AuthorizeAsync(
            "gh", new[] { "pr", "merge", "5" }, pipeName: fx.PipeName);

        Assert.Equal(GateDecisions.AllowOnce, first.Decision);
        Assert.Equal(PolicyReasonCodes.SessionAllow, newCommand.ReasonCode);
        Assert.Equal(PolicyReasonCodes.SessionAllow, sameClass.ReasonCode);
        Assert.Equal(1, gate.CardCount);
    }

    [Fact]
    public async Task A_ten_minute_click_shows_its_end_in_the_list_and_the_audit()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var gate = CountingGate.AllowForSession(SessionLength.TenMinutes);
        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "prompt",
            env: gate.AgentEnv);
        if (fx is null)
            return;
        await fx.SaveTokenAsync("timed-" + Guid.NewGuid().ToString("N"));

        var before = DateTimeOffset.UtcNow;
        var write = await AgentAuthorizeClient.AuthorizeAsync(
            "gh", new[] { "pr", "create", "--title", "t" }, pipeName: fx.PipeName);
        var again = await AgentAuthorizeClient.AuthorizeAsync(
            "gh", new[] { "pr", "merge", "5" }, pipeName: fx.PipeName);

        Assert.Equal(GateDecisions.SessionGrant, write.Decision);
        Assert.Equal(GateDecisions.SessionAllow, again.Decision);
        var row = Assert.Single(await AgentSessionsClient.ListAsync(fx.PipeName));
        var ends = DateTimeOffset.Parse(row.EndsUtc, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(ends, before.AddMinutes(10), DateTimeOffset.UtcNow.AddMinutes(10));
        Assert.Contains(fx.AuditLines(), l => l.Contains("\"session-grant\"", StringComparison.Ordinal)
            && l.Contains("\"10m\"", StringComparison.Ordinal));
    }
}


/// <summary>Fake Approval Gate helper that records every card it shows.</summary>
internal sealed class CountingGate : IAsyncDisposable
{
    private readonly string _helperPath;
    private readonly string _logPath;

    private CountingGate(string helperPath, string logPath)
    {
        _helperPath = helperPath;
        _logPath = logPath;
    }

    public static CountingGate AllowForSession(SessionLength length = SessionLength.UntilExit) =>
        Create(ApprovalHelperExitCodes.ForSession(length));

    public static CountingGate AllowOnce() => Create(ApprovalHelperExitCodes.AllowOnce);

    private static CountingGate Create(int exitCode)
    {
        var id = Guid.NewGuid().ToString("N");
        var logPath = Path.Combine(Path.GetTempPath(), "cw-cards-" + id + ".log");
        var helperPath = Path.Combine(Path.GetTempPath(), "cw-gate-" + id + ".cmd");
        File.WriteAllText(helperPath, $"""
            @echo off
            echo card>>"{logPath}"
            exit /b {exitCode}
            """);
        return new CountingGate(helperPath, logPath);
    }

    public IReadOnlyDictionary<string, string> AgentEnv =>
        new Dictionary<string, string> { [CmdWarden.Agent.Approval.ProcessApprovalGate.HelperPathEnvVar] = _helperPath };

    public int CardCount => File.Exists(_logPath) ? File.ReadAllLines(_logPath).Length : 0;

    public ValueTask DisposeAsync()
    {
        try { File.Delete(_helperPath); } catch { /* ignore */ }
        try { File.Delete(_logPath); } catch { /* ignore */ }
        return ValueTask.CompletedTask;
    }
}
