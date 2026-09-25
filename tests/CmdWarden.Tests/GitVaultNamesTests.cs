using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>GCM-shaped git vault keys and the get lookup rule (#205).</summary>
public class GitVaultNamesTests
{
    [Fact]
    public void Host_account_path_and_refresh_keys_are_GCM_shaped()
    {
        var host = GitVaultNames.Parse("https://github.com");
        Assert.Equal("https://github.com", host.HostKey);
        Assert.Equal(VaultNames.ProductPrefix + "git/https://github.com", GitVaultNames.Target(host.HostKey));

        var account = GitVaultNames.Parse("https://github.com", "alice");
        Assert.Equal("https://alice@github.com", account.AccountKey);
        Assert.Equal(VaultNames.ProductPrefix + "git/https://alice@github.com", GitVaultNames.Target(account.AccountKey));

        var path = GitVaultNames.Parse("https://dev.azure.com/org");
        Assert.Equal("https://dev.azure.com/org", path.HostKey);

        var refresh = GitVaultNames.Parse("https://github.com");
        Assert.Equal("https://oauth-refresh-token.github.com", refresh.RefreshKey);
        Assert.Equal(
            VaultNames.ProductPrefix + "git/https://oauth-refresh-token.github.com",
            GitVaultNames.Target(refresh.RefreshKey));
    }

    [Fact]
    public void Port_stays_on_the_host_and_trailing_path_slash_is_trimmed()
    {
        var ctx = GitVaultNames.Parse("https://github.com:8443/o/r.git/");
        Assert.Equal("github.com:8443", ctx.Host);
        Assert.Equal("o/r.git", ctx.Path);
        Assert.Equal("https://github.com:8443/o/r.git", ctx.HostKey);
    }

    [Fact]
    public void Username_on_get_selects_the_account_entry_then_the_host_entry()
    {
        var keys = new[] { "https://github.com", "https://alice@github.com", "https://bob@github.com" };

        Assert.Equal(
            new[] { "https://alice@github.com", "https://github.com" },
            GitVaultNames.LookupKeys("https://github.com", "alice", keys));
    }

    [Fact]
    public void No_username_selects_the_host_entry_before_a_single_account()
    {
        var keys = new[] { "https://alice@github.com", "https://github.com" };

        Assert.Equal(
            new[] { "https://github.com" },
            GitVaultNames.LookupKeys("https://GitHub.com", "", keys));
    }

    [Fact]
    public void No_username_and_one_account_selects_that_account()
    {
        var keys = new[] { "https://alice@github.com" };

        Assert.Equal(
            new[] { "https://alice@github.com" },
            GitVaultNames.LookupKeys("https://github.com", "", keys));
    }

    [Fact]
    public void Two_accounts_and_no_username_is_not_found()
    {
        var keys = new[] { "https://alice@github.com", "https://bob@github.com" };

        Assert.Empty(GitVaultNames.LookupKeys("https://github.com", "", keys));
    }

    [Fact]
    public void Path_is_part_of_the_key_only_when_sent()
    {
        var keys = new[] { "https://dev.azure.com", "https://dev.azure.com/org" };

        Assert.Equal(
            new[] { "https://dev.azure.com/org" },
            GitVaultNames.LookupKeys("https://dev.azure.com/org", "", keys));
        Assert.Equal(
            new[] { "https://dev.azure.com" },
            GitVaultNames.LookupKeys("https://dev.azure.com", "", keys));
    }

    [Fact]
    public void Refresh_token_entry_resolves_as_a_host_key()
    {
        var keys = new[] { "https://oauth-refresh-token.gitlab.com" };

        Assert.Equal(
            new[] { "https://oauth-refresh-token.gitlab.com" },
            GitVaultNames.LookupKeys("https://oauth-refresh-token.gitlab.com", "", keys));
    }

    [Fact]
    public void Store_key_is_account_qualified_when_username_is_set()
    {
        Assert.Equal("https://alice@github.com", GitVaultNames.StoreKey("https://github.com", "alice"));
        Assert.Equal("https://github.com", GitVaultNames.StoreKey("https://github.com", ""));
    }
}
