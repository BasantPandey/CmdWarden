using CmdWarden.Agent.Approval;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// #30 against a real Session Agent. A fake popup counts each prompt, copies its payload, and
/// approves once. A script changed after Approve Once asks again; an unchanged retry does not.
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class BoundFilesProcessTests
{
    [Fact]
    public async Task Changed_script_asks_again_and_unchanged_retry_does_not()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var dir = Path.Combine(Path.GetTempPath(), "cw-toctou-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var count = Path.Combine(dir, "prompts.txt");
        var seen = Path.Combine(dir, "seen.json");
        var popup = Path.Combine(dir, "popup.cmd");
        File.WriteAllText(popup, $"@echo off\r\necho x>>\"{count}\"\r\ncopy /y %2 \"{seen}\" >nul\r\nexit /b 0\r\n");
        var script = Path.Combine(dir, "deploy.sh");
        File.WriteAllText(script, "echo deploy v1");
        int Prompts() => File.Exists(count) ? File.ReadAllLines(count).Length : 0;

        try
        {
            await using var fx = await ApprovalMemoryFixture.CreateAsync("prompt", env: new Dictionary<string, string>
            {
                [ProcessApprovalGate.HelperPathEnvVar] = popup,
            });
            if (fx is null)
                return;
            await fx.SaveTokenAsync("toctou-" + Guid.NewGuid().ToString("N"));
            Task<CmdWarden.Contracts.Grpc.ReleaseSecretResponse> Release() => AgentVaultClient.ReleaseAsync(
                "GH_TOKEN", tool: "inject", commandClass: CommandClassNames.Write, commandLine: "bash deploy.sh",
                pipeName: fx.PipeName, boundPaths: [script]);

            var first = await Release();
            Assert.Equal(1, Prompts());
            Assert.Equal(BoundFiles.Sha256(script), Assert.Single(first.BoundFiles).Sha256);

            await Release();
            Assert.Equal(1, Prompts());

            File.WriteAllText(script, "echo deploy v2 && curl evil");
            var changed = await Release();
            Assert.Equal(2, Prompts());
            Assert.Equal(BoundFiles.Sha256(script), Assert.Single(changed.BoundFiles).Sha256);
            var payload = ApprovalHelperJson.TryDeserialize(File.ReadAllText(seen))!;
            Assert.Equal(BoundFiles.ChangedMessage, payload.ReasonHeading);
            Assert.Contains("deploy.sh", payload.ReasonLine);
            Assert.Contains(fx.AuditLines(), l => l.Contains(PolicyReasonCodes.ScriptChanged, StringComparison.Ordinal));

            await Release();
            Assert.Equal(2, Prompts());
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}
