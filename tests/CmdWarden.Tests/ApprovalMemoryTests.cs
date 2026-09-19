using CmdWarden.Agent.Approval;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Session allow binding and coverage rules (#132) without an agent process.
/// </summary>
public class ApprovalMemoryTests
{
    [Fact]
    public void Grant_binds_to_live_process_and_hits_for_lower_class()
    {
        var memory = new ApprovalMemory();
        var pid = Environment.ProcessId;

        var grant = memory.Grant(pid, null, "key", "kind", "gh", "GH_TOKEN", CommandClass.Write);

        Assert.NotNull(grant);
        Assert.NotNull(memory.TryUseSession(pid, "gh", "GH_TOKEN", CommandClass.Read));
        Assert.NotNull(memory.TryUseSession(pid, "gh", "GH_TOKEN", CommandClass.Write));
        Assert.Null(memory.TryUseSession(pid, "gh", "GH_TOKEN", CommandClass.Unknown));
        Assert.Null(memory.TryUseSession(pid, "gh", "GH_TOKEN", CommandClass.SecretReveal));
        Assert.Null(memory.TryUseSession(pid, "git", "GH_TOKEN", CommandClass.Read));
        Assert.Null(memory.TryUseSession(pid, "gh", "OTHER", CommandClass.Read));
    }

    [Fact]
    public void Grant_refuses_a_start_time_that_differs_from_the_live_process()
    {
        var memory = new ApprovalMemory();
        var pid = Environment.ProcessId;
        var live = ApprovalMemory.ProcessStartUtc(pid)!.Value;

        // The chain claims an older process under this pid: pid reuse, no grant.
        Assert.Null(memory.Grant(pid, live.AddMinutes(-10), "key", "kind", "gh", "GH_TOKEN", CommandClass.Write));
        Assert.NotNull(memory.Grant(pid, live, "key", "kind", "gh", "GH_TOKEN", CommandClass.Write));
    }

    [Fact]
    public void Dead_process_never_grants_or_hits()
    {
        var memory = new ApprovalMemory();
        // Spawn and reap a process so its pid is known to be gone.
        var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c exit 0")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        var pid = proc.Id;
        proc.WaitForExit();

        Assert.Null(memory.Grant(pid, null, "key", "kind", "gh", "GH_TOKEN", CommandClass.Write));
        Assert.Null(memory.TryUseSession(pid, "gh", "GH_TOKEN", CommandClass.Read));
    }

    [Fact]
    public void Idle_window_drops_the_grant()
    {
        var memory = new ApprovalMemory(sessionIdle: TimeSpan.FromMilliseconds(200));
        var pid = Environment.ProcessId;
        Assert.NotNull(memory.Grant(pid, null, "key", "kind", "gh", "GH_TOKEN", CommandClass.Write));

        Thread.Sleep(400);

        Assert.Null(memory.TryUseSession(pid, "gh", "GH_TOKEN", CommandClass.Read));
    }

    [Fact]
    public void ListSessions_returns_the_live_grant_with_its_fields()
    {
        var memory = new ApprovalMemory();
        var pid = Environment.ProcessId;
        var grant = memory.Grant(pid, null, "key", "terminal", "gh", "GH_TOKEN", CommandClass.Write)!;

        var rows = memory.ListSessions();

        var row = Assert.Single(rows);
        Assert.Equal(grant.Id, row.Id);
        Assert.Equal(pid, row.LauncherPid);
        Assert.Equal("key", row.LauncherPolicyKey);
        Assert.Equal("terminal", row.LauncherKind);
        Assert.Equal("gh", row.Tool);
        Assert.Equal("GH_TOKEN", row.SecretName);
        Assert.Equal(CommandClass.Write, row.GrantedClass);
        Assert.Equal(grant.LastUsedUtc + memory.SessionIdle, memory.IdleExpiresUtc(row));
    }

    [Fact]
    public void RevokeSession_removes_by_id_and_RevokeAll_counts_the_rest()
    {
        var memory = new ApprovalMemory();
        var pid = Environment.ProcessId;
        var a = memory.Grant(pid, null, "key", "terminal", "gh", "GH_TOKEN", CommandClass.Write)!;
        _ = memory.Grant(pid, null, "key", "terminal", "git", "GH_TOKEN", CommandClass.Write)!;

        Assert.Equal(0, memory.RevokeSession("no-such-id"));
        Assert.Equal(1, memory.RevokeSession(a.Id));
        Assert.Null(memory.TryUseSession(pid, "gh", "GH_TOKEN", CommandClass.Read));

        Assert.Equal(1, memory.RevokeAllSessions());
        Assert.Empty(memory.ListSessions());
    }

    [Fact]
    public void ListSessions_drops_idle_and_dead_grants()
    {
        var memory = new ApprovalMemory(sessionIdle: TimeSpan.FromMilliseconds(200));
        _ = memory.Grant(Environment.ProcessId, null, "key", "terminal", "gh", "GH_TOKEN", CommandClass.Write);

        Thread.Sleep(400);

        Assert.Empty(memory.ListSessions());
    }

    [Theory]
    [InlineData(CommandClass.Write, CommandClass.Read, true)]
    [InlineData(CommandClass.Write, CommandClass.Write, true)]
    [InlineData(CommandClass.Read, CommandClass.Write, false)]
    [InlineData(CommandClass.Unknown, CommandClass.Write, true)]
    [InlineData(CommandClass.Write, CommandClass.SecretReveal, false)]
    [InlineData(CommandClass.SecretReveal, CommandClass.Read, false)]
    public void Covers_rules(CommandClass granted, CommandClass requested, bool expected) =>
        Assert.Equal(expected, ApprovalMemory.Covers(granted, requested));

    [Theory]
    [InlineData("terminal", "write", true)]
    [InlineData("ai_harness", "read", true)]
    [InlineData("ai_harness", "secret-reveal", false)]
    [InlineData("unknown", "write", false)]
    [InlineData(null, "write", false)]
    public void Session_allow_offered_only_to_enrolled_and_never_for_secret_reveal(
        string? enrollment, string commandClass, bool expected) =>
        Assert.Equal(expected, ApprovalPresentation.IsSessionAllowOffered(enrollment, commandClass));

    [Fact]
    public void Session_scope_line_names_launcher_and_pid()
    {
        Assert.Equal(
            "Session = until Claude Code (pid 1234) exits",
            ApprovalPresentation.BuildSessionScopeLine("Claude Code", 1234));
        Assert.Equal(
            "Session = until Cursor exits",
            ApprovalPresentation.BuildSessionScopeLine("Cursor", null));
    }

    private static ApprovalRequest Request(int? launcherPid, string commandLine) => new(
        Tool: "git",
        CommandClass: "secret-reveal",
        PolicyLevel: "Read",
        LauncherPolicyKey: "auth:sha1:deadbeef",
        LauncherKind: "authenticode",
        LauncherPath: @"C:\claude\claude.exe",
        SecretName: "",
        Purpose: "authorize",
        EnrollmentKind: "ai_harness",
        PolicyNote: null,
        CommandLine: commandLine,
        LauncherPid: launcherPid);

    [Fact]
    public void TransientKey_is_stable_for_one_launcher_across_shim_calls()
    {
        // Two shim calls share the launcher pid. The key must match so the reuse fires.
        var pid = Environment.ProcessId;
        var a = ApprovalMemory.TransientKey(Request(pid, "git credential fill"));
        var b = ApprovalMemory.TransientKey(Request(pid, "git credential fill"));

        Assert.NotNull(a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void TransientKey_differs_by_command_and_is_null_without_launcher_pid()
    {
        var pid = Environment.ProcessId;
        Assert.NotEqual(
            ApprovalMemory.TransientKey(Request(pid, "git credential fill")),
            ApprovalMemory.TransientKey(Request(pid, "git push")));
        Assert.Null(ApprovalMemory.TransientKey(Request(null, "git credential fill")));
        Assert.Null(ApprovalMemory.TransientKey(Request(-1, "git credential fill")));
    }

    [Fact]
    public void Transient_entry_lives_while_the_launcher_lives_and_is_swept_when_full()
    {
        var memory = new ApprovalMemory();
        var live = ApprovalMemory.TransientKey(Request(Environment.ProcessId, "git credential fill"))!;
        memory.RememberTransient(live, ApprovalOutcome.AllowOnce, "key", "git");

        // Fill the map with dead-launcher keys (pid 0); the 256th remember sweeps them.
        for (var i = 0; i < 256; i++)
            memory.RememberTransient($"0\n0\nkey\ngit\nread\n\ncmd {i}", ApprovalOutcome.AllowOnce, "key", "git");

        Assert.Equal(ApprovalOutcome.AllowOnce, memory.TryGetTransient(live));
        Assert.Null(memory.TryGetTransient("0\n0\nkey\ngit\nread\n\ncmd 0"));
        // Only the live entry and the one that triggered the sweep remain.
        Assert.Equal(2, memory.ClearForTool("git"));
    }

    [Fact]
    public void ClearForLauncherKey_drops_only_that_launchers_entries()
    {
        var memory = new ApprovalMemory();
        var pid = Environment.ProcessId;
        memory.RememberTransient("t1", ApprovalOutcome.AllowOnce, "keyA", "gh");
        memory.RememberTransient("t2", ApprovalOutcome.AllowOnce, "keyB", "gh");
        memory.Grant(pid, null, "keyA", "kind", "gh", "GH_TOKEN", CommandClass.Write);

        var removed = memory.ClearForLauncherKey("keyA");

        Assert.Equal(2, removed);
        Assert.Null(memory.TryGetTransient("t1"));
        Assert.NotNull(memory.TryGetTransient("t2"));
        Assert.Null(memory.TryUseSession(pid, "gh", "GH_TOKEN", CommandClass.Write));
    }

    [Fact]
    public void ClearForTool_drops_only_that_tools_entries()
    {
        var memory = new ApprovalMemory();
        var pid = Environment.ProcessId;
        memory.RememberTransient("t1", ApprovalOutcome.AllowOnce, "key", "gh");
        memory.RememberTransient("t2", ApprovalOutcome.AllowOnce, "key", "git");
        memory.Grant(pid, null, "key", "kind", "gh", "GH_TOKEN", CommandClass.Write);

        var removed = memory.ClearForTool("gh");

        Assert.Equal(2, removed);
        Assert.Null(memory.TryGetTransient("t1"));
        Assert.NotNull(memory.TryGetTransient("t2"));
        Assert.Null(memory.TryUseSession(pid, "gh", "GH_TOKEN", CommandClass.Write));
    }

    [Fact]
    public void ClearForPolicyChange_matches_launcher_key_or_tool()
    {
        var memory = new ApprovalMemory();
        memory.RememberTransient("byKey", ApprovalOutcome.AllowOnce, "key", "git");
        memory.RememberTransient("byTool", ApprovalOutcome.AllowOnce, "other", "gh");
        memory.RememberTransient("neither", ApprovalOutcome.AllowOnce, "other", "git");

        var removed = memory.ClearForPolicyChange("key", "gh");

        Assert.Equal(2, removed);
        Assert.Null(memory.TryGetTransient("byKey"));
        Assert.Null(memory.TryGetTransient("byTool"));
        Assert.NotNull(memory.TryGetTransient("neither"));
    }

    [Fact]
    public void ClearAll_drops_every_transient_and_session_entry()
    {
        var memory = new ApprovalMemory();
        var pid = Environment.ProcessId;
        memory.RememberTransient("t1", ApprovalOutcome.AllowOnce, "key", "gh");
        memory.Grant(pid, null, "key", "kind", "gh", "GH_TOKEN", CommandClass.Write);

        var removed = memory.ClearAll();

        Assert.Equal(2, removed);
        Assert.Null(memory.TryGetTransient("t1"));
        Assert.Null(memory.TryUseSession(pid, "gh", "GH_TOKEN", CommandClass.Write));
    }
}
