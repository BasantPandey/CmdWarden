using CmdWarden.Agent.Approval;
using CmdWarden.Cli;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// #24: the agent tells the popup when Hello is required, and audits the Hello result.
/// A fake popup (.cmd) copies its payload and exits with a Hello exit code.
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class WindowsHelloProcessTests
{
    private static readonly string[] AuthToken = ["auth", "token"];

    private sealed class FakePopup : IDisposable
    {
        public string Dir { get; } = Path.Combine(Path.GetTempPath(), "cw-hello-" + Guid.NewGuid().ToString("N"));
        public string Exe => Path.Combine(Dir, "popup.cmd");
        public string Seen => Path.Combine(Dir, "seen.json");

        public FakePopup(int exitCode)
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Exe, $"@echo off\r\ncopy /y %2 \"{Seen}\" >nul\r\nexit /b {exitCode}\r\n");
        }

        public ApprovalHelperPayload Payload() => ApprovalHelperJson.TryDeserialize(File.ReadAllText(Seen))!;

        public void Dispose()
        {
            try { Directory.Delete(Dir, recursive: true); } catch { /* ignore */ }
        }
    }

    private static Task<ApprovalMemoryFixture?> Fixture(FakePopup popup) =>
        ApprovalMemoryFixture.CreateAsync("prompt", env: new Dictionary<string, string>
        {
            [ProcessApprovalGate.HelperPathEnvVar] = popup.Exe,
        });

    [Fact]
    public async Task Secret_reveal_asks_for_Hello_and_audits_HelloVerified()
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var popup = new FakePopup(ApprovalHelperExitCodes.FromAnswer(ApprovalHelperExitCodes.AllowOnce, HelloCheck.Verified));
        await using var fx = await Fixture(popup);
        if (fx is null)
            return;
        await fx.SaveTokenAsync("hello-" + Guid.NewGuid().ToString("N"));

        var grant = await AgentAuthorizeClient.AuthorizeAsync("gh", AuthToken, pipeName: fx.PipeName);

        Assert.True(grant.Allowed);
        Assert.Equal(PolicyReasonCodes.HelloVerified, grant.ReasonCode);
        Assert.True(popup.Payload().HelloRequired);
        Assert.Contains(fx.AuditLines(), l => l.Contains("\"decision\":\"allow-once\"", StringComparison.Ordinal)
            && l.Contains(PolicyReasonCodes.HelloVerified, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cancelled_Hello_denies()
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var popup = new FakePopup(ApprovalHelperExitCodes.FromAnswer(ApprovalHelperExitCodes.Deny, HelloCheck.Canceled));
        await using var fx = await Fixture(popup);
        if (fx is null)
            return;

        var denied = await Assert.ThrowsAsync<Grpc.Core.RpcException>(() =>
            AgentAuthorizeClient.AuthorizeAsync("gh", AuthToken, pipeName: fx.PipeName));

        Assert.Equal(Grpc.Core.StatusCode.PermissionDenied, denied.StatusCode);
        Assert.Contains(PolicyReasonCodes.UserDenied, denied.Status.Detail, StringComparison.Ordinal);
        Assert.Contains(fx.AuditLines(), l => l.Contains("\"decision\":\"deny\"", StringComparison.Ordinal)
            && l.Contains(PolicyReasonCodes.HelloCanceled, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Hello_not_set_up_falls_back_and_notes_it_in_the_audit()
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var popup = new FakePopup(ApprovalHelperExitCodes.FromAnswer(ApprovalHelperExitCodes.AllowOnce, HelloCheck.NotAvailable));
        await using var fx = await Fixture(popup);
        if (fx is null)
            return;
        await fx.SaveTokenAsync("hello-" + Guid.NewGuid().ToString("N"));

        var grant = await AgentAuthorizeClient.AuthorizeAsync("gh", AuthToken, pipeName: fx.PipeName);

        Assert.True(grant.Allowed);
        Assert.Contains(fx.AuditLines(), l => l.Contains(PolicyReasonCodes.HelloUnavailable, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Hello_off_does_not_ask()
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var popup = new FakePopup(ApprovalHelperExitCodes.AllowOnce);
        await using var fx = await Fixture(popup);
        if (fx is null)
            return;
        var store = new PolicyStore(fx.PolicyPath);
        store.Load();
        store.SetHelloMode(WindowsHelloPolicy.Off);
        store.Save();
        await fx.SaveTokenAsync("hello-" + Guid.NewGuid().ToString("N"));

        var grant = await AgentAuthorizeClient.AuthorizeAsync("gh", AuthToken, pipeName: fx.PipeName);

        Assert.True(grant.Allowed);
        Assert.False(popup.Payload().HelloRequired);
        Assert.Equal("", grant.ReasonCode);
    }
}
