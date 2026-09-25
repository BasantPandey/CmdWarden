using System.Text;
using CmdWarden.Cli;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>#27 CheckLeak and #29 canary alarm against a real Session Agent.</summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class LeakGuardProcessTests
{
    private const string CanaryKeyId = "AKIACANARYTEST000001";

    private static void WriteCanaries(string productRoot) =>
        new CanaryStore(productRoot).Save(
        [
            new CanaryEntry(CanaryStore.FileKind, @"C:\fake\.aws\credentials", "", [new CanaryToken("AWS_ACCESS_KEY_ID", CanaryKeyId)]),
            new CanaryEntry(CanaryStore.VaultKind, "CW_CANARY_VAULT", "", [new CanaryToken("CW_CANARY_VAULT", "ghp_canaryvaultvalue000000000000000000")]),
        ]);

    [Fact]
    public async Task CheckLeak_replaces_vaulted_values_and_audits_the_name()
    {
        if (!OperatingSystem.IsWindows())
            return;
        await using var fx = await ApprovalMemoryFixture.CreateAsync("deny");
        if (fx is null)
            return;
        var name = "CW_LEAK_" + Guid.NewGuid().ToString("N")[..8];
        var value = "leak-" + Guid.NewGuid().ToString("N");
        await AgentVaultClient.SaveAsync(name, Encoding.UTF8.GetBytes(value), fx.PipeName);
        try
        {
            var response = await AgentLeakClient.CheckAsync([$"token: {value}", "nothing here"], "claude:Bash", fx.PipeName);

            Assert.Equal($"token: [CmdWarden: {name}]", response.Texts[0]);
            Assert.Equal("nothing here", response.Texts[1]);
            Assert.Equal([name], response.Names);
            Assert.False(response.Canary);
            var lines = fx.AuditLines();
            Assert.Contains(lines, l => l.Contains("\"decision\":\"redact\"", StringComparison.Ordinal)
                && l.Contains(name, StringComparison.Ordinal) && l.Contains("claude:Bash", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, l => l.Contains(value, StringComparison.Ordinal));
        }
        finally
        {
            await AgentVaultClient.DeleteAsync(name, fx.PipeName);
        }
    }

    [Fact]
    public async Task Canary_seen_by_the_leak_guard_blocks_the_launcher()
    {
        if (!OperatingSystem.IsWindows())
            return;
        await using var fx = await ApprovalMemoryFixture.CreateAsync("allow");
        if (fx is null)
            return;
        WriteCanaries(fx.ProductRoot);

        var response = await AgentLeakClient.CheckAsync([$"aws_access_key_id = {CanaryKeyId}"], "claude:Read", fx.PipeName);
        Assert.True(response.Canary);
        Assert.Equal("aws_access_key_id = [CmdWarden: AWS_ACCESS_KEY_ID]", response.Texts[0]);

        var denied = await Assert.ThrowsAsync<Grpc.Core.RpcException>(() =>
            AgentAuthorizeClient.AuthorizeAsync("gh", ["pr", "list"], pipeName: fx.PipeName));
        Assert.Contains(PolicyReasonCodes.CanaryHit, denied.Status.Detail, StringComparison.Ordinal);
        Assert.True(fx.AuditLines().Count(l => l.Contains(PolicyReasonCodes.CanaryHit, StringComparison.Ordinal)) >= 2);
    }

    [Fact]
    public async Task Shim_call_with_a_canary_in_the_env_is_denied_and_raises_the_alarm()
    {
        if (!OperatingSystem.IsWindows())
            return;
        await using var fx = await ApprovalMemoryFixture.CreateAsync("allow");
        if (fx is null)
            return;
        WriteCanaries(fx.ProductRoot);
        await fx.SaveTokenAsync("canary-env-" + Guid.NewGuid().ToString("N"));

        var denied = await Assert.ThrowsAsync<Grpc.Core.RpcException>(() =>
            AgentAuthorizeClient.AuthorizeAsync("gh", ["api", "user"], pipeName: fx.PipeName,
                envValueHashes: [ValueHash.Of("some-other-value"), ValueHash.Of(CanaryKeyId)]));

        Assert.Equal(Grpc.Core.StatusCode.PermissionDenied, denied.StatusCode);
        Assert.Contains(PolicyReasonCodes.CanaryHit, denied.Status.Detail, StringComparison.Ordinal);
        // The alarm stays: a clean call from the same launcher is denied too.
        var again = await Assert.ThrowsAsync<Grpc.Core.RpcException>(() =>
            AgentAuthorizeClient.AuthorizeAsync("gh", ["pr", "list"], pipeName: fx.PipeName));
        Assert.Contains(PolicyReasonCodes.CanaryHit, again.Status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Release_of_the_canary_vault_entry_is_denied()
    {
        if (!OperatingSystem.IsWindows())
            return;
        await using var fx = await ApprovalMemoryFixture.CreateAsync("allow");
        if (fx is null)
            return;
        WriteCanaries(fx.ProductRoot);

        var denied = await Assert.ThrowsAsync<Grpc.Core.RpcException>(() =>
            AgentVaultClient.ReleaseAsync("CW_CANARY_VAULT", purpose: "inject", tool: "inject",
                commandClass: CommandClassNames.Read, pipeName: fx.PipeName));
        Assert.Contains(PolicyReasonCodes.CanaryHit, denied.Status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clean_shim_call_with_env_hashes_still_runs()
    {
        if (!OperatingSystem.IsWindows())
            return;
        await using var fx = await ApprovalMemoryFixture.CreateAsync("allow");
        if (fx is null)
            return;
        WriteCanaries(fx.ProductRoot);
        await fx.SaveTokenAsync("canary-clean-" + Guid.NewGuid().ToString("N"));

        var grant = await AgentAuthorizeClient.AuthorizeAsync("gh", ["pr", "list"], pipeName: fx.PipeName,
            envValueHashes: ValueHash.OfEnvironment());
        Assert.True(grant.Allowed);
    }
}
