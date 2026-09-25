using CmdWarden.Agent.Approval;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Seam: Approval Gate prompt text + mode factory (issue #6).
/// </summary>
public class ApprovalGateTests
{
    [Fact]
    public void Prompt_text_includes_tool_launcher_and_class()
    {
        var body = ApprovalPromptText.BuildBody(new ApprovalRequest(
            Tool: "gh",
            CommandClass: "secret-reveal",
            PolicyLevel: "Trusted",
            LauncherPolicyKey: "auth:sha1:abc",
            LauncherKind: "authenticode",
            LauncherPath: @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
            SecretName: "GH_TOKEN",
            Purpose: "export",
            EnrollmentKind: "terminal",
            PolicyNote: null));

        Assert.Contains("Tool:            gh", body);
        Assert.Contains("Command class:   secret-reveal", body);
        Assert.Contains("Policy level:    Trusted", body);
        Assert.Contains("auth:sha1:abc", body);
        Assert.Contains("powershell.exe", body);
        Assert.Contains("GH_TOKEN", body);
        Assert.Contains("Approve Once", body);
        Assert.Contains("Deny", body);
        Assert.Equal("CmdWarden", ApprovalPromptText.Caption);
    }

    [Theory]
    [InlineData("allow", ApprovalOutcome.AllowOnce)]
    [InlineData("deny", ApprovalOutcome.Deny)]
    [InlineData("auto-deny", ApprovalOutcome.Deny)]
    [InlineData("session", ApprovalOutcome.AllowForSession)]
    public void Scripted_modes_return_fixed_outcome(string mode, ApprovalOutcome expected)
    {
        var gate = ApprovalGateFactory.Create(mode);
        var outcome = gate.Prompt(MinimalRequest()).Outcome;
        Assert.Equal(expected, outcome);
    }

    [Theory]
    [InlineData("off")]
    [InlineData("headless")]
    [InlineData("unavailable")]
    public void Off_mode_is_unavailable(string mode)
    {
        var gate = ApprovalGateFactory.Create(mode);
        Assert.Equal(ApprovalOutcome.Unavailable, gate.Prompt(MinimalRequest()).Outcome);
    }

    [Fact]
    public void Default_and_prompt_modes_use_process_gate()
    {
        // Ticket #82: interactive default is process helper (modern card).
        var gate = ApprovalGateFactory.Create(null);
        Assert.IsType<ProcessApprovalGate>(gate);
        gate = ApprovalGateFactory.Create("prompt");
        Assert.IsType<ProcessApprovalGate>(gate);
        gate = ApprovalGateFactory.Create("winui");
        Assert.IsType<ProcessApprovalGate>(gate);
    }

    [Fact]
    public void Messagebox_mode_is_native_gate()
    {
        var gate = ApprovalGateFactory.Create("messagebox");
        Assert.IsType<NativeApprovalGate>(gate);
        gate = ApprovalGateFactory.Create("native");
        Assert.IsType<NativeApprovalGate>(gate);
    }

    [Fact]
    public void Process_gate_missing_helper_is_unavailable()
    {
        var gate = new ProcessApprovalGate(
            resolveHelperPath: () => null,
            startProcess: _ => throw new InvalidOperationException("must not start"),
            timeout: TimeSpan.FromSeconds(1));
        Assert.Equal(ApprovalOutcome.Unavailable, gate.Prompt(MinimalRequest()).Outcome);
    }

    private static ApprovalRequest MinimalRequest() => new(
        Tool: "inject",
        CommandClass: "write",
        PolicyLevel: "Deny",
        LauncherPolicyKey: "unknown",
        LauncherKind: "unknown",
        LauncherPath: null,
        SecretName: "X",
        Purpose: "test",
        EnrollmentKind: "unknown",
        PolicyNote: PolicyReasonCodes.NotEnrolled);
}
