using System.Text;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Strong gh store (#208) on a private CredMan namespace: stock prefix <c>lgt-&lt;id&gt;:</c>,
/// vault prefix <c>CmdWarden/lgt-&lt;id&gt;/</c>, temp hosts.yml. The real gh entries stay untouched.
/// </summary>
public class GhStrongStoreTests
{
    private sealed class Sandbox : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "cw-ghstrong-" + Guid.NewGuid().ToString("N"));
        public string Id { get; } = "lgt-" + Guid.NewGuid().ToString("N")[..8];
        public string HostsPath => Path.Combine(Root, "hosts.yml");
        public string FakeGh => Path.Combine(Root, "gh.cmd");
        public CredentialVault Vault { get; } = new();
        public GhStrongStore Store { get; }

        public Sandbox()
        {
            Directory.CreateDirectory(Root);
            Store = new GhStrongStore(Vault, "CmdWarden/" + Id + "/", Id + ":", HostsPath);
            // Exit 1 for a token that contains "bad"; gh auth status is otherwise fine.
            File.WriteAllText(FakeGh, """
                @echo off
                echo %GH_TOKEN%%GH_ENTERPRISE_TOKEN% | findstr /C:"bad" >nul && exit /b 1
                exit /b 0
                """);
        }

        public void WriteStock(string host, string user, string token) =>
            Vault.SaveTarget(Store.StockPrefix + host + ":" + user, user.Length == 0 ? null : user, Encoding.UTF8.GetBytes(token), comment: "");

        public IReadOnlyList<string> StockTargets() => Vault.ListTargets(Store.StockPrefix).Select(t => t.Target).ToList();

        public string? Vaulted(string user, string host) =>
            Store.Read(user, host) is { } b ? Encoding.UTF8.GetString(b) : null;

        public void Dispose()
        {
            foreach (var t in Vault.ListTargets(Store.StockPrefix).Concat(Vault.ListTargets(Store.VaultPrefix)))
                Vault.DeleteTarget(t.Target);
            try { Directory.Delete(Root, recursive: true); } catch { /* ignore */ }
        }
    }

    private const string Hosts = """
        github.com:
            users:
                alice:
                bob:
            user: alice
            git_protocol: https
        ghes.example.test:
            users:
                carol:
                    oauth_token: tok-carol
            user: carol
            oauth_token: tok-carol
        """;

    [Fact]
    public void Migrate_vaults_slot_and_user_entries_strips_hosts_and_deletes_stock()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var sb = new Sandbox();
        File.WriteAllText(sb.HostsPath, Hosts);
        sb.WriteStock("github.com", "", "tok-alice");
        sb.WriteStock("github.com", "alice", "tok-alice");
        sb.WriteStock("github.com", "bob", "tok-bob");

        var result = sb.Store.Migrate(sb.FakeGh);

        Assert.Equal(
            new[] { "alice@github.com", "bob@github.com", "carol@ghes.example.test", "ghes.example.test", "github.com" },
            result.Migrated.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(3, result.Deleted.Count);
        Assert.True(result.HostsStripped);
        Assert.Empty(sb.StockTargets());
        Assert.Equal("tok-alice", sb.Vaulted("", "github.com"));
        Assert.Equal("tok-bob", sb.Vaulted("bob", "github.com"));
        Assert.Equal("tok-carol", sb.Vaulted("", "ghes.example.test"));
        Assert.Equal("tok-carol", sb.Vaulted("carol", "ghes.example.test"));

        var hosts = GhHostsFile.Read(sb.HostsPath);
        Assert.DoesNotContain("oauth_token", File.ReadAllText(sb.HostsPath));
        Assert.Equal(new[] { "alice", "bob" }, hosts[0].Users);
        Assert.Equal("alice", hosts[0].ActiveUser);
        Assert.Equal("carol", hosts[1].ActiveUser);
        Assert.Contains("git_protocol: https", File.ReadAllText(sb.HostsPath));

        // Idempotent: nothing left to move.
        var again = sb.Store.Migrate(sb.FakeGh);
        Assert.Empty(again.Migrated);
        Assert.False(again.HostsStripped);
    }

    [Fact]
    public void Different_vault_value_fails_whole_and_removes_copies_made_in_this_run()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var sb = new Sandbox();
        sb.WriteStock("github.com", "alice", "tok-alice");
        sb.WriteStock("github.com", "zed", "tok-zed");
        sb.Store.Save("zed", "github.com", "other"u8);

        var ex = Assert.Throws<InvalidOperationException>(() => sb.Store.Migrate(sb.FakeGh));
        Assert.Contains("zed@github.com", ex.Message);
        Assert.Equal(2, sb.StockTargets().Count);
        Assert.Null(sb.Vaulted("alice", "github.com"));
        Assert.Equal("other", sb.Vaulted("zed", "github.com"));
    }

    [Fact]
    public void Failed_verify_fails_whole_before_any_delete()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var sb = new Sandbox();
        sb.WriteStock("github.com", "alice", "tok-alice");
        sb.WriteStock("ghes.example.test", "carol", "tok-bad");

        var ex = Assert.Throws<InvalidOperationException>(() => sb.Store.Migrate(sb.FakeGh));
        Assert.Contains("ghes.example.test", ex.Message);
        Assert.Equal(2, sb.StockTargets().Count);
        Assert.Empty(sb.Store.Keys());
    }

    [Fact]
    public void Reconcile_drops_entries_hosts_yml_no_longer_lists()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var sb = new Sandbox();
        sb.Store.Save("", "github.com", "tok-alice"u8);
        sb.Store.Save("alice", "github.com", "tok-alice"u8);
        sb.Store.Save("bob", "github.com", "tok-bob"u8);
        sb.Store.Save("", "ghes.example.test", "tok-carol"u8);
        sb.Store.Save("carol", "ghes.example.test", "tok-carol"u8);
        File.WriteAllText(sb.HostsPath, "github.com:\n    users:\n        alice:\n    user: alice\n");

        var deleted = sb.Store.Reconcile();

        Assert.Equal(new[] { "bob@github.com", "carol@ghes.example.test", "ghes.example.test" }, deleted.OrderBy(d => d));
        Assert.Equal(new[] { ("", "github.com"), ("alice", "github.com") }, sb.Store.Keys().OrderBy(k => k.User));
    }

    [Fact]
    public void WriteBack_restores_stock_layout_and_active_slot_from_hosts_yml()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var sb = new Sandbox();
        File.WriteAllText(sb.HostsPath, "github.com:\n    users:\n        alice:\n        bob:\n    user: bob\n");
        sb.Store.Save("alice", "github.com", "tok-alice"u8);
        sb.Store.Save("bob", "github.com", "tok-bob"u8);

        var restored = sb.Store.WriteBack();

        Assert.Equal(3, restored.Count);
        Assert.Empty(sb.Store.Keys());
        var slot = sb.Vault.ReadTarget(sb.Store.StockPrefix + "github.com:")!;
        Assert.Equal("tok-bob", Encoding.UTF8.GetString(slot.Blob));
        var alice = sb.Vault.ReadTarget(sb.Store.StockPrefix + "github.com:alice")!;
        Assert.Equal("alice", alice.UserName);
        Assert.Equal("tok-alice", Encoding.UTF8.GetString(alice.Blob));
    }

    [Fact]
    public void Hosts_file_parse_keeps_users_active_user_and_tokens()
    {
        var hosts = GhHostsFile.Parse(Hosts.Split('\n'));
        Assert.Equal(2, hosts.Count);
        Assert.Equal("github.com", hosts[0].Host);
        Assert.Empty(hosts[0].Tokens);
        Assert.Equal("tok-carol", hosts[1].Tokens["carol"]);
        Assert.Equal("tok-carol", hosts[1].Tokens[""]);
        var stripped = GhHostsFile.Strip(Hosts.Split('\n'))!;
        Assert.DoesNotContain(stripped, l => l.Contains("oauth_token"));
        Assert.Equal(Hosts.Split('\n').Length - 2, stripped.Length);
        Assert.Null(GhHostsFile.Strip(stripped));
    }

    [Theory]
    [InlineData("github.com", false)]
    [InlineData("GitHub.com", false)]
    [InlineData("tenant.ghe.com", false)]
    [InlineData("github.localhost", false)]
    [InlineData("ghes.example.test", true)]
    public void IsEnterprise_follows_the_gh_host_rule(string host, bool expected) =>
        Assert.Equal(expected, GhVaultNames.IsEnterprise(host));

    [Theory]
    [InlineData(new[] { "pr", "list", "--hostname", "GHES.example.test" }, null, "ghes.example.test")]
    [InlineData(new[] { "api", "--hostname=ghes.example.test", "user" }, null, "ghes.example.test")]
    [InlineData(new[] { "pr", "list", "-R", "ghes.example.test/org/repo" }, null, "ghes.example.test")]
    [InlineData(new[] { "pr", "list", "--repo", "https://ghes.example.test/org/repo" }, null, "ghes.example.test")]
    [InlineData(new[] { "pr", "list", "-R", "org/repo" }, null, null)]
    [InlineData(new[] { "pr", "list" }, "ghes.example.test", "ghes.example.test")]
    [InlineData(new[] { "pr", "list", "--hostname", "a.test" }, "b.test", "a.test")]
    public void NamedHost_reads_hostname_repo_and_GH_HOST(string[] argv, string? env, string? expected) =>
        Assert.Equal(expected, GhCommandClassifier.NamedHost(argv, env));
}
