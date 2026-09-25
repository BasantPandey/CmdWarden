using CmdWarden.Cli;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>#32: the agent reason is one clean line of at most 200 characters.</summary>
public class AgentReasonTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(" \r\n\t ", null)]
    [InlineData("create the release PR", "create the release PR")]
    [InlineData("  line one\r\n\r\nline two  ", "line one line two")]
    [InlineData("safe‮gnp.exe", "safe gnp.exe")]
    [InlineData("a\u0000b\u001Bc", "a b c")]
    [InlineData("zero​width", "zero width")]
    public void Clean_gives_one_plain_line(string? raw, string? expected) =>
        Assert.Equal(expected, AgentReason.Clean(raw));

    [Fact]
    public void Clean_limits_the_length_marks_the_cut_and_never_splits_a_pair()
    {
        var exact = new string('x', AgentReason.MaxLength);
        Assert.Equal(exact, AgentReason.Clean(exact));

        var cut = AgentReason.Clean(new string('x', 500))!;
        Assert.Equal(AgentReason.MaxLength, cut.Length);
        Assert.EndsWith("x…", cut);

        var pair = AgentReason.Clean(new string('x', AgentReason.MaxLength - 2) + "\U0001F600\U0001F600")!;
        Assert.Equal(new string('x', AgentReason.MaxLength - 2) + "…", pair);
    }

    [Fact]
    public void Card_and_text_body_show_the_reason_with_the_label()
    {
        var request = new ApprovalRequest("gh", "write", "Read", "key", "authenticode", null, "GH_TOKEN",
            "authorize", "ai-harness", null, CommandLine: "gh pr create", AgentReason: "create the release PR");

        Assert.Equal("create the release PR", ApprovalPresentation.ToHelperPayload(request).AgentReason);
        Assert.Contains("The agent says: create the release PR", ApprovalPromptText.BuildBody(request));
        Assert.DoesNotContain(AgentReason.Label, ApprovalPromptText.BuildBody(request with { AgentReason = null }));
        Assert.Null(ApprovalPresentation.ToHelperPayload(request with { AgentReason = null }).AgentReason);
    }

    [Fact]
    public void Audit_line_shows_the_reason_only_when_set()
    {
        const string with = """{"ts":"2026-09-26T10:00:00Z","decision":"allow-once","tool":"gh","agentReason":"create the release PR"}""";
        const string without = """{"ts":"2026-09-26T10:00:00Z","decision":"allow-once","tool":"gh","agentReason":null}""";
        Assert.Contains("agent-says=\"create the release PR\"", AuditFormatter.FormatLine(with));
        Assert.DoesNotContain("agent-says", AuditFormatter.FormatLine(without));
        Assert.Equal("create the release PR", AuditGateRecord.TryParse(with)!.AgentReason);
    }
}
