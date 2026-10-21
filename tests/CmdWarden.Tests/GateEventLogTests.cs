using System.Diagnostics;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>#48: gate decisions in the Windows Event Log. The tests do not write to the real log.</summary>
public class GateEventLogTests
{
    [Fact]
    public void EventFor_gives_one_id_per_decision_type()
    {
        string[] decisions =
        [
            GateDecisions.AutoAllow, GateDecisions.AllowOnce, GateDecisions.SessionGrant, GateDecisions.SessionAllow,
            GateDecisions.Deny, GateDecisions.Unavailable, GateDecisions.Redact,
        ];
        var ids = decisions.Select(d => GateEventLog.EventFor(d).Id).ToList();
        Assert.Equal(decisions.Length, ids.Distinct().Count());
        Assert.DoesNotContain(GateEventLog.OtherId, ids);

        Assert.Equal((1001, EventLogEntryType.Information), GateEventLog.EventFor(GateDecisions.AutoAllow));
        Assert.Equal((2001, EventLogEntryType.Warning), GateEventLog.EventFor(GateDecisions.Deny));
        Assert.Equal((3001, EventLogEntryType.Warning), GateEventLog.EventFor(GateDecisions.Redact));
        Assert.Equal((GateEventLog.OtherId, EventLogEntryType.Information), GateEventLog.EventFor("new-kind"));
    }

    [Fact]
    public void Message_has_the_audit_fields_and_leaves_out_empty_ones()
    {
        var message = GateEventLog.Message(new AuditGateRecord
        {
            Ts = "2026-10-21T10:04:00.0000000Z",
            Decision = GateDecisions.Deny,
            ReasonCode = "human-deny",
            Tool = "gh",
            CommandClass = "secret-reveal",
            PolicyLevel = "Read",
            LauncherPolicyKey = "path:cursor",
            LauncherKind = "path",
            EnrollmentKind = "ai-harness",
            SecretName = "GH_TOKEN",
            ClientPid = 4242,
        });

        Assert.Equal(
            "CmdWarden gate decision: deny\r\n" +
            "Tool: gh\r\n" +
            "Command class: secret-reveal\r\n" +
            "Policy level: Read\r\n" +
            "Secret: GH_TOKEN\r\n" +
            "Reason code: human-deny\r\n" +
            "Launcher: path:cursor (path, ai-harness)\r\n" +
            "Client pid: 4242\r\n" +
            "Time (UTC): 2026-10-21T10:04:00.0000000Z",
            message);
    }

    [Fact]
    public void Message_keeps_the_agent_reason_on_one_line()
    {
        var message = GateEventLog.Message(new AuditGateRecord
        {
            Decision = GateDecisions.AllowOnce,
            Tool = "gh",
            AgentReason = "list PRs\r\nTool: fake‮",
        });

        Assert.Contains("\r\nAgent says: list PRs Tool: fake\r\n", message, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\nTool: fake", message, StringComparison.Ordinal);
    }
}
