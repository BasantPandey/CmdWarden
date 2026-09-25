using Grpc.Core;
using CmdWarden.Cli;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// HelperCredential RPC end to end (#202). The test host's parent process stands in for the
/// pinned real docker.exe. The chain rule then sees a pinned, signed tool directly above the caller.
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class HelperCredentialProcessTests
{
    private static readonly string[] DockerPull = { "pull", "alpine" };

    private static string NewUrl() => "https://registry-" + Guid.NewGuid().ToString("N")[..8] + ".example.test/v1/";

    private static void DeleteEntry(string url) =>
        new CredentialVault().DeleteTarget(VaultNames.HelperTargetName("docker", url));

    [Fact]
    public async Task Store_get_list_erase_round_trip_with_one_audit_row_each()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "off", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        Assert.NotNull(TestChain.PinParentAsDocker(fx.ProductRoot));
        var url = NewUrl();
        var secret = "reg-secret-" + Guid.NewGuid().ToString("N");
        try
        {
            var stored = await AgentHelperClient.CredentialAsync("docker", "store", url, "alice", secret, fx.PipeName);
            Assert.Equal(CommandClassNames.Write, stored.CommandClass);
            Assert.Equal(GateDecisions.AutoAllow, stored.Decision);

            var got = await AgentHelperClient.CredentialAsync("docker", "get", url, pipeName: fx.PipeName);
            Assert.Equal(CommandClassNames.Read, got.CommandClass);
            Assert.Equal("alice", got.Username);
            Assert.Equal(secret, got.Secret);

            var listed = await AgentHelperClient.CredentialAsync("docker", "list", pipeName: fx.PipeName);
            var entry = Assert.Single(listed.Entries, e => e.ServerUrl == url);
            Assert.Equal("alice", entry.Username);

            Assert.DoesNotContain(await AgentVaultClient.ListSecretNamesAsync(fx.PipeName), n => n.Contains(url, StringComparison.Ordinal));

            var erased = await AgentHelperClient.CredentialAsync("docker", "erase", url, pipeName: fx.PipeName);
            Assert.Equal(CommandClassNames.Write, erased.CommandClass);

            var missing = await Assert.ThrowsAsync<RpcException>(() =>
                AgentHelperClient.CredentialAsync("docker", "get", url, pipeName: fx.PipeName));
            Assert.Equal(StatusCode.NotFound, missing.StatusCode);

            var rows = fx.AuditLines().Where(l => l.Contains("\"tool\":\"docker\"", StringComparison.Ordinal)).ToList();
            foreach (var purpose in new[] { "helper-store", "helper-get", "helper-list", "helper-erase" })
                Assert.Contains(rows, r => r.Contains($"\"purpose\":\"{purpose}\"", StringComparison.Ordinal));
            Assert.Equal(4, rows.Count(r => r.Contains(url, StringComparison.Ordinal)));
            Assert.Single(rows, r => r.Contains($"\"secretName\":\"{VaultNames.ProductPrefix}docker/*\"", StringComparison.Ordinal));
            Assert.DoesNotContain(rows, r => r.Contains(secret, StringComparison.Ordinal));
        }
        finally
        {
            DeleteEntry(url);
        }
    }

    [Fact]
    public async Task Get_denies_HelperParentMissing_when_the_pinned_docker_is_not_above()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "off", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        new ToolPinStore(fx.ProductRoot).Save("docker", Path.Combine(Environment.SystemDirectory, "cmd.exe"));

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            AgentHelperClient.CredentialAsync("docker", "get", "https://x.example.test/", pipeName: fx.PipeName));
        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
        Assert.Contains(PolicyReasonCodes.HelperParentMissing, ex.Status.Detail, StringComparison.Ordinal);

        var row = Assert.Single(fx.AuditLines(), l => l.Contains("\"tool\":\"docker\"", StringComparison.Ordinal));
        Assert.Contains("\"decision\":\"deny\"", row, StringComparison.Ordinal);
        Assert.Contains(PolicyReasonCodes.HelperParentMissing, row, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_denies_HelperParentMissing_when_docker_has_no_pin()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "off", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            AgentHelperClient.CredentialAsync("docker", "get", "https://x.example.test/", pipeName: fx.PipeName));
        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
        Assert.Contains(PolicyReasonCodes.HelperParentMissing, ex.Status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_record_covers_get_but_not_store_for_a_Deny_launcher()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // Scripted allow: the shim grant is a human allow-once; a covered get is auto-allow + RunCovered.
        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "allow", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        Assert.NotNull(TestChain.PinParentAsDocker(fx.ProductRoot));
        var url = NewUrl();
        try
        {
            new CredentialVault().SaveTarget(VaultNames.HelperTargetName("docker", url), "bob", "s3cret"u8);
            fx.SetLevel("docker", PolicyLevel.Deny);

            var grant = await AgentAuthorizeClient.AuthorizeAsync("docker", DockerPull, pipeName: fx.PipeName);
            Assert.Equal(GateDecisions.AllowOnce, grant.Decision);

            var got = await AgentHelperClient.CredentialAsync("docker", "get", url, pipeName: fx.PipeName);
            Assert.Equal("s3cret", got.Secret);
            Assert.Equal(GateDecisions.AutoAllow, got.Decision);
            Assert.Equal(PolicyReasonCodes.RunCovered, got.ReasonCode);

            var store = await AgentHelperClient.CredentialAsync("docker", "store", url, "bob", "s3cret", fx.PipeName);
            Assert.Equal(GateDecisions.AllowOnce, store.Decision);
            Assert.Equal("", store.ReasonCode);

            var covered = Assert.Single(fx.AuditLines(), l => l.Contains(PolicyReasonCodes.RunCovered, StringComparison.Ordinal));
            Assert.Contains("\"purpose\":\"helper-get\"", covered, StringComparison.Ordinal);
        }
        finally
        {
            DeleteEntry(url);
        }
    }

    [Fact]
    public async Task Policy_change_clears_the_run_record()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "off", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        Assert.NotNull(TestChain.PinParentAsDocker(fx.ProductRoot));
        var url = NewUrl();
        try
        {
            new CredentialVault().SaveTarget(VaultNames.HelperTargetName("docker", url), "bob", "s3cret"u8);
            var grant = await AgentAuthorizeClient.AuthorizeAsync("docker", DockerPull, pipeName: fx.PipeName);
            Assert.True(grant.Allowed);

            fx.SetLevel("docker", PolicyLevel.Deny);

            var ex = await Assert.ThrowsAsync<RpcException>(() =>
                AgentHelperClient.CredentialAsync("docker", "get", url, pipeName: fx.PipeName));
            Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
        }
        finally
        {
            DeleteEntry(url);
        }
    }

    [Fact]
    public async Task Get_without_a_run_record_prompts_for_a_Deny_launcher()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "off", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        Assert.NotNull(TestChain.PinParentAsDocker(fx.ProductRoot));
        fx.SetLevel("docker", PolicyLevel.Deny);

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            AgentHelperClient.CredentialAsync("docker", "get", "https://x.example.test/", pipeName: fx.PipeName));
        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
        Assert.Contains(PolicyReasonCodes.ApprovalUnavailable, ex.Status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_only_audit_dir_blocks_get_with_no_value()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "off", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        Assert.NotNull(TestChain.PinParentAsDocker(fx.ProductRoot));
        var url = NewUrl();
        try
        {
            _ = await AgentHelperClient.CredentialAsync("docker", "store", url, "carol", "s3cret", fx.PipeName);
            using var readOnly = new ReadOnlyDir(Path.Combine(fx.ProductRoot, "audit"));

            var ex = await Assert.ThrowsAsync<RpcException>(() =>
                AgentHelperClient.CredentialAsync("docker", "get", url, pipeName: fx.PipeName));
            Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
            Assert.Contains(PolicyReasonCodes.ApprovalUnavailable, ex.Status.Detail, StringComparison.Ordinal);
        }
        finally
        {
            DeleteEntry(url);
        }
    }

    [Theory]
    [InlineData("az", "get")]
    [InlineData("docker", "version")]
    public async Task Unsupported_tool_or_action_is_InvalidArgument(string tool, string action)
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            AgentHelperClient.CredentialAsync(tool, action, "https://x.example.test/", pipeName: fx.PipeName));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }
}
