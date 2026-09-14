using Grpc.Core;
using CmdWarden.Agent.Identity;
using CmdWarden.Cli;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Git HelperCredential (#205). The test host's parent stands in for the pinned git.exe,
/// so the signer walk sees a pinned tool at depth 1.
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class GitHelperCredentialProcessTests
{
    private static readonly string[] GitPush = { "push", "origin", "main" };
    private static readonly string[] GitFill = { "credential", "fill" };

    private static string NewHost() => "https://git-" + Guid.NewGuid().ToString("N")[..8] + ".example.test";

    private static string? PinParentAsGit(string productRoot)
    {
        var chain = new ProcessChainWalker().Walk(Environment.ProcessId);
        var parent = chain.Count > 1 ? chain[1].Path : null;
        if (parent is null)
            return null;
        new ToolPinStore(productRoot).Save("git", parent);
        return parent;
    }

    private static void DeleteGit(string serverUrl, params string[] usernames)
    {
        var vault = new CredentialVault();
        var names = usernames.Length == 0 ? new[] { "" } : usernames;
        foreach (var user in names)
        {
            var ctx = GitVaultNames.Parse(serverUrl, user);
            vault.DeleteTarget(GitVaultNames.Target(ctx.HostKey));
            vault.DeleteTarget(GitVaultNames.Target(ctx.AccountKey));
            vault.DeleteTarget(GitVaultNames.Target(ctx.RefreshKey));
        }
    }

    [Fact]
    public async Task Host_account_path_and_refresh_entries_resolve()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "off", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        Assert.NotNull(PinParentAsGit(fx.ProductRoot));

        var host = NewHost();
        var pathUrl = host + "/org";
        var refreshHost = "https://oauth-refresh-token." + host["https://".Length..];
        var secret = "tok-" + Guid.NewGuid().ToString("N");
        var vault = new CredentialVault();
        try
        {
            vault.SaveTarget(GitVaultNames.Target(host), "alice", CredentialVault.Utf8Bytes(secret), comment: "1700000000");
            vault.SaveTarget(GitVaultNames.Target("https://alice@" + host["https://".Length..]), "alice", CredentialVault.Utf8Bytes(secret + "-acct"));
            vault.SaveTarget(GitVaultNames.Target(pathUrl), "alice", CredentialVault.Utf8Bytes(secret + "-path"));
            vault.SaveTarget(GitVaultNames.Target(refreshHost), "alice", CredentialVault.Utf8Bytes("refresh-tok"));

            var byAccount = await AgentHelperClient.CredentialAsync(
                "git", "get", host, username: "alice", pipeName: fx.PipeName);
            Assert.Equal(secret + "-acct", byAccount.Secret);
            Assert.Equal("alice", byAccount.Username);

            var byHost = await AgentHelperClient.CredentialAsync("git", "get", host, pipeName: fx.PipeName);
            Assert.Equal(secret, byHost.Secret);
            Assert.Equal("1700000000", byHost.PasswordExpiryUtc);

            var byPath = await AgentHelperClient.CredentialAsync("git", "get", pathUrl, pipeName: fx.PipeName);
            Assert.Equal(secret + "-path", byPath.Secret);

            var byRefresh = await AgentHelperClient.CredentialAsync("git", "get", refreshHost, pipeName: fx.PipeName);
            Assert.Equal("refresh-tok", byRefresh.Secret);

            Assert.DoesNotContain(
                await AgentVaultClient.ListSecretNamesAsync(fx.PipeName),
                n => n.Contains(host, StringComparison.Ordinal));
        }
        finally
        {
            DeleteGit(host, "", "alice");
            DeleteGit(pathUrl, "alice");
            new CredentialVault().DeleteTarget(GitVaultNames.Target(refreshHost));
        }
    }

    [Fact]
    public async Task Two_accounts_and_no_username_is_not_found()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "off", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        Assert.NotNull(PinParentAsGit(fx.ProductRoot));

        var host = NewHost();
        var vault = new CredentialVault();
        try
        {
            vault.SaveTarget(GitVaultNames.Target("https://alice@" + host["https://".Length..]), "alice", "a-tok"u8);
            vault.SaveTarget(GitVaultNames.Target("https://bob@" + host["https://".Length..]), "bob", "b-tok"u8);

            var missing = await Assert.ThrowsAsync<RpcException>(() =>
                AgentHelperClient.CredentialAsync("git", "get", host, pipeName: fx.PipeName));
            Assert.Equal(StatusCode.NotFound, missing.StatusCode);

            var alice = await AgentHelperClient.CredentialAsync(
                "git", "get", host, username: "alice", pipeName: fx.PipeName);
            Assert.Equal("a-tok", alice.Secret);
        }
        finally
        {
            DeleteGit(host, "alice", "bob");
        }
    }

    [Fact]
    public async Task Equal_store_writes_a_row_and_skips_the_gate()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "off", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        Assert.NotNull(PinParentAsGit(fx.ProductRoot));
        var host = NewHost();
        var secret = "same-" + Guid.NewGuid().ToString("N");
        try
        {
            _ = await AgentHelperClient.CredentialAsync("git", "store", host, "alice", secret, fx.PipeName);

            fx.SetLevel("git", PolicyLevel.Deny);
            var again = await AgentHelperClient.CredentialAsync("git", "store", host, "alice", secret, fx.PipeName);
            Assert.Equal(CommandClassNames.Write, again.CommandClass);
            Assert.Equal(GateDecisions.AutoAllow, again.Decision);
            Assert.Equal(PolicyReasonCodes.Unchanged, again.ReasonCode);

            var row = Assert.Single(fx.AuditLines(), l => l.Contains(PolicyReasonCodes.Unchanged, StringComparison.Ordinal));
            Assert.Contains("\"purpose\":\"helper-store\"", row, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, row, StringComparison.Ordinal);
        }
        finally
        {
            DeleteGit(host, "alice");
        }
    }

    [Fact]
    public async Task Different_store_is_gated()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "off", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        Assert.NotNull(PinParentAsGit(fx.ProductRoot));
        var host = NewHost();
        try
        {
            _ = await AgentHelperClient.CredentialAsync("git", "store", host, "alice", "first-secret", fx.PipeName);
            fx.SetLevel("git", PolicyLevel.Deny);

            var ex = await Assert.ThrowsAsync<RpcException>(() =>
                AgentHelperClient.CredentialAsync("git", "store", host, "alice", "second-secret", fx.PipeName));
            Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
            Assert.Contains(PolicyReasonCodes.ApprovalUnavailable, ex.Status.Detail, StringComparison.Ordinal);
        }
        finally
        {
            DeleteGit(host, "alice");
        }
    }

    [Fact]
    public async Task Ephemeral_store_is_never_written()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "off", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        Assert.NotNull(PinParentAsGit(fx.ProductRoot));
        var host = NewHost();
        try
        {
            var stored = await AgentHelperClient.CredentialAsync(
                "git", "store", host, "alice", "ephem-secret", fx.PipeName, ephemeral: true);
            Assert.Equal(PolicyReasonCodes.Unchanged, stored.ReasonCode);

            var missing = await Assert.ThrowsAsync<RpcException>(() =>
                AgentHelperClient.CredentialAsync("git", "get", host, username: "alice", pipeName: fx.PipeName));
            Assert.Equal(StatusCode.NotFound, missing.StatusCode);
        }
        finally
        {
            DeleteGit(host, "alice");
        }
    }

    [Fact]
    public async Task Equal_erase_is_gated_and_mismatch_is_a_noop()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "off", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        Assert.NotNull(PinParentAsGit(fx.ProductRoot));
        var host = NewHost();
        var secret = "keep-" + Guid.NewGuid().ToString("N");
        try
        {
            _ = await AgentHelperClient.CredentialAsync("git", "store", host, "alice", secret, fx.PipeName);
            fx.SetLevel("git", PolicyLevel.Deny);

            var mismatch = await AgentHelperClient.CredentialAsync(
                "git", "erase", host, "alice", "wrong-secret", fx.PipeName);
            Assert.Equal(PolicyReasonCodes.Unchanged, mismatch.ReasonCode);

            fx.SetLevel("git", PolicyLevel.Trusted);
            var stillThere = await AgentHelperClient.CredentialAsync(
                "git", "get", host, username: "alice", pipeName: fx.PipeName);
            Assert.Equal(secret, stillThere.Secret);

            fx.SetLevel("git", PolicyLevel.Deny);
            var ex = await Assert.ThrowsAsync<RpcException>(() =>
                AgentHelperClient.CredentialAsync("git", "erase", host, "alice", secret, fx.PipeName));
            Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
            Assert.Contains(PolicyReasonCodes.ApprovalUnavailable, ex.Status.Detail, StringComparison.Ordinal);

            fx.SetLevel("git", PolicyLevel.Trusted);
            var afterDeniedErase = await AgentHelperClient.CredentialAsync(
                "git", "get", host, username: "alice", pipeName: fx.PipeName);
            Assert.Equal(secret, afterDeniedErase.Secret);
        }
        finally
        {
            DeleteGit(host, "alice");
        }
    }

    [Fact]
    public async Task Credential_fill_grant_does_not_cover_get()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "allow", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        Assert.NotNull(PinParentAsGit(fx.ProductRoot));
        var host = NewHost();
        var secret = "fill-" + Guid.NewGuid().ToString("N");
        try
        {
            new CredentialVault().SaveTarget(GitVaultNames.Target(host), "alice", CredentialVault.Utf8Bytes(secret));
            fx.SetLevel("git", PolicyLevel.Deny);

            var fill = await AgentAuthorizeClient.AuthorizeAsync("git", GitFill, pipeName: fx.PipeName);
            Assert.Equal(GateDecisions.AllowOnce, fill.Decision);

            var afterFill = await AgentHelperClient.CredentialAsync("git", "get", host, pipeName: fx.PipeName);
            Assert.Equal(secret, afterFill.Secret);
            Assert.Equal(GateDecisions.AllowOnce, afterFill.Decision);
            Assert.NotEqual(PolicyReasonCodes.RunCovered, afterFill.ReasonCode);
        }
        finally
        {
            DeleteGit(host, "alice", "");
        }
    }

    [Fact]
    public async Task Push_grant_covers_get()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "allow", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        Assert.NotNull(PinParentAsGit(fx.ProductRoot));
        var host = NewHost();
        var secret = "push-" + Guid.NewGuid().ToString("N");
        try
        {
            new CredentialVault().SaveTarget(GitVaultNames.Target(host), "alice", CredentialVault.Utf8Bytes(secret));
            fx.SetLevel("git", PolicyLevel.Deny);

            var push = await AgentAuthorizeClient.AuthorizeAsync("git", GitPush, pipeName: fx.PipeName);
            Assert.Equal(GateDecisions.AllowOnce, push.Decision);

            var got = await AgentHelperClient.CredentialAsync("git", "get", host, pipeName: fx.PipeName);
            Assert.Equal(secret, got.Secret);
            Assert.Equal(GateDecisions.AutoAllow, got.Decision);
            Assert.Equal(PolicyReasonCodes.RunCovered, got.ReasonCode);
        }
        finally
        {
            DeleteGit(host, "alice", "");
        }
    }

    [Fact]
    public async Task Store_with_refresh_token_round_trips_on_get()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "off", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        Assert.NotNull(PinParentAsGit(fx.ProductRoot));
        var host = NewHost();
        try
        {
            _ = await AgentHelperClient.CredentialAsync(
                "git", "store", host, "alice", "pat-token", fx.PipeName,
                passwordExpiryUtc: "1800000000",
                oauthRefreshToken: "rt-1");

            var got = await AgentHelperClient.CredentialAsync(
                "git", "get", host, username: "alice", pipeName: fx.PipeName);
            Assert.Equal("pat-token", got.Secret);
            Assert.Equal("1800000000", got.PasswordExpiryUtc);
            Assert.Equal("rt-1", got.OauthRefreshToken);
        }
        finally
        {
            DeleteGit(host, "alice");
        }
    }
}
