using System.Diagnostics;
using System.Text.Json;
using CmdWarden.Agent.Approval;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Seam: IApprovalGate process helper adapter (tickets #79 / #80) — fake process only.
/// </summary>
public class ProcessApprovalGateTests
{
    private static ApprovalRequest MinimalRequest() => new(
        Tool: "gh",
        CommandClass: "secret-reveal",
        PolicyLevel: "Read",
        LauncherPolicyKey: "auth:sha1:abc",
        LauncherKind: "authenticode",
        LauncherPath: @"C:\Apps\Cursor.exe",
        SecretName: "GH_TOKEN",
        Purpose: "test",
        EnrollmentKind: "ai-harness",
        PolicyNote: null,
        CommandLine: "gh auth token",
        ToolPath: @"C:\Program Files\GitHub CLI\gh.exe",
        WorkingDirectory: @"C:\work");

    [Theory]
    [InlineData(0, ApprovalOutcome.AllowOnce)]
    [InlineData(1, ApprovalOutcome.Deny)]
    [InlineData(2, ApprovalOutcome.Unavailable)]
    [InlineData(99, ApprovalOutcome.Unavailable)]
    public void Exit_codes_map_to_outcomes(int exitCode, ApprovalOutcome expected)
    {
        var gate = new ProcessApprovalGate(
            resolveHelperPath: () => @"C:\fake\CmdWarden.ApprovalGate.exe",
            startProcess: _ => CreateExitedProcess(exitCode),
            timeout: TimeSpan.FromSeconds(5));

        // File.Exists check will fail for fake path — use a real temp file as "helper"
        var helper = Path.Combine(Path.GetTempPath(), "cw-fake-helper-" + Guid.NewGuid().ToString("n") + ".exe");
        File.WriteAllBytes(helper, new byte[] { 0x4D, 0x5A }); // minimal MZ stub so Exists is true
        try
        {
            gate = new ProcessApprovalGate(
                resolveHelperPath: () => helper,
                startProcess: _ => CreateExitedProcess(exitCode),
                timeout: TimeSpan.FromSeconds(5));
            Assert.Equal(expected, gate.Prompt(MinimalRequest()).Outcome);
        }
        finally
        {
            try { File.Delete(helper); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Missing_helper_is_unavailable()
    {
        var gate = new ProcessApprovalGate(
            resolveHelperPath: () => null,
            startProcess: _ => throw new InvalidOperationException("should not start"),
            timeout: TimeSpan.FromSeconds(2));

        Assert.Equal(ApprovalOutcome.Unavailable, gate.Prompt(MinimalRequest()).Outcome);
    }

    [Fact]
    public void Payload_file_contains_secret_names_not_values()
    {
        string? capturedArgs = null;
        string? payloadPath = null;
        var helper = Path.Combine(Path.GetTempPath(), "cw-fake-helper-" + Guid.NewGuid().ToString("n") + ".exe");
        File.WriteAllBytes(helper, new byte[] { 0x4D, 0x5A });
        try
        {
            var gate = new ProcessApprovalGate(
                resolveHelperPath: () => helper,
                startProcess: psi =>
                {
                    capturedArgs = psi.Arguments;
                    var m = System.Text.RegularExpressions.Regex.Match(psi.Arguments, "--payload \"(.+?)\"");
                    if (m.Success)
                        payloadPath = m.Groups[1].Value;
                    // Read before gate deletes
                    return CreateExitedProcess(ApprovalHelperExitCodes.Deny);
                },
                timeout: TimeSpan.FromSeconds(5));

            // Race: process exits immediately so finally may delete before we read.
            // Capture JSON inside startProcess instead.
            string? jsonSnapshot = null;
            gate = new ProcessApprovalGate(
                resolveHelperPath: () => helper,
                startProcess: psi =>
                {
                    capturedArgs = psi.Arguments;
                    var m = System.Text.RegularExpressions.Regex.Match(psi.Arguments, "--payload \"(.+?)\"");
                    if (m.Success && File.Exists(m.Groups[1].Value))
                        jsonSnapshot = File.ReadAllText(m.Groups[1].Value);
                    return CreateExitedProcess(ApprovalHelperExitCodes.AllowOnce);
                },
                timeout: TimeSpan.FromSeconds(5));

            Assert.Equal(ApprovalOutcome.AllowOnce, gate.Prompt(MinimalRequest()).Outcome);
            Assert.NotNull(jsonSnapshot);
            Assert.Contains("GH_TOKEN", jsonSnapshot);
            Assert.Contains("gh auth token", jsonSnapshot);
            Assert.DoesNotContain("gho_", jsonSnapshot);
            Assert.DoesNotContain("\"secretValue\"", jsonSnapshot, StringComparison.OrdinalIgnoreCase);
            var payload = ApprovalHelperJson.TryDeserialize(jsonSnapshot!);
            Assert.NotNull(payload);
            Assert.Equal("Cursor.exe", payload!.LauncherDisplayName); // file name from path without PE
            Assert.Contains("GH_TOKEN", payload.SecretNames);
        }
        finally
        {
            try { File.Delete(helper); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Factory_winui_mode_selects_process_gate()
    {
        var gate = ApprovalGateFactory.Create("winui");
        Assert.IsType<ProcessApprovalGate>(gate);
        gate = ApprovalGateFactory.Create("helper");
        Assert.IsType<ProcessApprovalGate>(gate);
    }

    [Theory]
    [InlineData(0, ApprovalOutcome.AllowOnce, HelloCheck.NotAsked)]
    [InlineData(1, ApprovalOutcome.Deny, HelloCheck.NotAsked)]
    [InlineData(2, ApprovalOutcome.Unavailable, HelloCheck.NotAsked)]
    [InlineData(3, ApprovalOutcome.AllowForSession, HelloCheck.NotAsked)]
    [InlineData(4, ApprovalOutcome.Unavailable, HelloCheck.NotAsked)]
    [InlineData(-1, ApprovalOutcome.Unavailable, HelloCheck.NotAsked)]
    [InlineData(0x10, ApprovalOutcome.AllowOnce, HelloCheck.Verified)]
    [InlineData(0x13, ApprovalOutcome.AllowForSession, HelloCheck.Verified)]
    [InlineData(0x20, ApprovalOutcome.AllowOnce, HelloCheck.NotAvailable)]
    [InlineData(0x41, ApprovalOutcome.Deny, HelloCheck.Canceled)]
    // A flag on the wrong answer fails closed.
    [InlineData(0x11, ApprovalOutcome.Unavailable, HelloCheck.NotAsked)]
    [InlineData(0x40, ApprovalOutcome.Unavailable, HelloCheck.NotAsked)]
    [InlineData(0x30, ApprovalOutcome.Unavailable, HelloCheck.NotAsked)]
    [InlineData(0x12, ApprovalOutcome.Unavailable, HelloCheck.NotAsked)]
    public void Exit_code_helper_mapping(int exitCode, ApprovalOutcome outcome, HelloCheck hello)
    {
        var answer = ApprovalHelperExitCodes.ToAnswer(exitCode);
        Assert.Equal(outcome, answer.Outcome);
        Assert.Equal(hello, answer.Hello);
    }

    [Theory]
    [InlineData(ApprovalHelperExitCodes.AllowOnce, HelloCheck.Verified)]
    [InlineData(ApprovalHelperExitCodes.AllowForSession, HelloCheck.NotAvailable)]
    [InlineData(ApprovalHelperExitCodes.Deny, HelloCheck.Canceled)]
    [InlineData(ApprovalHelperExitCodes.AllowOnce, HelloCheck.NotAsked)]
    public void Exit_code_round_trips(int baseCode, HelloCheck hello) =>
        Assert.Equal(hello, ApprovalHelperExitCodes.ToAnswer(ApprovalHelperExitCodes.FromAnswer(baseCode, hello)).Hello);

    /// <summary>Process that has already exited with the given code (no real child).</summary>
    private static Process CreateExitedProcess(int exitCode)
    {
        // Start a real short-lived process that exits with code via cmd.
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c exit {exitCode}",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var p = Process.Start(psi)!;
        p.WaitForExit(5000);
        return p;
    }
}
