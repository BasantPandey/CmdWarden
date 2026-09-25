using System.Text;
using CmdWarden.Cli.Harden;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// cw harden gh --strong and cw unharden gh (#209) on a private stock namespace and a temp
/// hosts.yml. Vault entries use the real CmdWarden/gh/ prefix with unique test hosts; the probe
/// reads the same namespace through CW_GH_STOCK_PREFIX and GH_CONFIG_DIR.
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class GhStrongHardenTests
{
    private sealed class Sandbox : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "cw-ghharden-" + Guid.NewGuid().ToString("N"));
        public string Suffix { get; } = Guid.NewGuid().ToString("N")[..8] + ".example.test";
        public string StockPrefix { get; } = "lgt-" + Guid.NewGuid().ToString("N")[..8] + ":";
        public string ConfigDir => Path.Combine(Root, "ghconfig");
        public string HostsPath => Path.Combine(ConfigDir, "hosts.yml");
        public string ShimsDir => Path.Combine(Root, "shims");
        public string FakeGh => Path.Combine(Root, "gh.cmd");
        public CredentialVault Vault { get; } = new();
        public GhStrongStore Store { get; }
        public string Host1 => "one." + Suffix;
        public string Host2 => "two." + Suffix;

        public Sandbox()
        {
            Directory.CreateDirectory(ConfigDir);
            Directory.CreateDirectory(ShimsDir);
            File.WriteAllBytes(Path.Combine(ShimsDir, "gh.exe"), [0]);
            File.WriteAllText(FakeGh, """
                @echo off
                echo %GH_TOKEN%%GH_ENTERPRISE_TOKEN% | findstr /C:"bad" >nul && exit /b 1
                exit /b 0
                """);
            new ToolPinStore(Root).Save("gh", FakeGh);
            Store = new GhStrongStore(Vault, null, StockPrefix, HostsPath);
            Environment.SetEnvironmentVariable("CW_GH_STOCK_PREFIX", StockPrefix);
            Environment.SetEnvironmentVariable("GH_CONFIG_DIR", ConfigDir);
        }

        public GhStrongOptions Options(string? token = null, string? hostname = null) => new()
        {
            ProductRoot = Root,
            Store = Store,
            TokenOverride = token,
            Hostname = hostname ?? GhVaultNames.DefaultHost,
        };

        public void WriteStock(string host, string user, string token) =>
            Vault.SaveTarget(StockPrefix + host + ":" + user, user.Length == 0 ? null : user, Encoding.UTF8.GetBytes(token), comment: "");

        public IReadOnlyList<string> StockTargets() => Vault.ListTargets(StockPrefix).Select(t => t.Target).ToList();

        public string? Vaulted(string user, string host) =>
            Store.Read(user, host) is { } b ? Encoding.UTF8.GetString(b) : null;

        public HardenedToolStatus Probe() =>
            HardenedToolStatus.Probe("gh", Root, path: [new PathEntry(ShimsDir, "user")]);

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("CW_GH_STOCK_PREFIX", null);
            Environment.SetEnvironmentVariable("GH_CONFIG_DIR", null);
            foreach (var t in Vault.ListTargets(StockPrefix))
                Vault.DeleteTarget(t.Target);
            foreach (var t in Vault.ListTargets(GhVaultNames.Prefix))
            {
                if (t.Target.Contains(Suffix, StringComparison.OrdinalIgnoreCase))
                    Vault.DeleteTarget(t.Target);
            }
            try { Directory.Delete(Root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Harden_migrates_records_hosts_and_probe_counts_hosts_and_accounts()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var sb = new Sandbox();
        File.WriteAllText(sb.HostsPath, $"{sb.Host1}:\n    users:\n        alice:\n        bob:\n    user: alice\n    git_protocol: https\n");
        sb.WriteStock(sb.Host1, "", "tok-alice");
        sb.WriteStock(sb.Host1, "alice", "tok-alice");
        sb.WriteStock(sb.Host1, "bob", "tok-bob");

        var result = GhStrongHarden.Migrate(sb.Options());

        Assert.Equal(3, result.Migrated.Count);
        Assert.Equal(3, result.Deleted.Count);
        Assert.Empty(sb.StockTargets());
        var pin = new ToolPinStore(sb.Root).TryGet("gh")!;
        Assert.True(pin.IsStrong);
        var host = Assert.Single(pin.Strong!.Hosts!);
        Assert.Equal(sb.Host1, host.Host);
        Assert.Equal(new[] { "alice", "bob" }, host.Users);
        Assert.Equal("alice", host.ActiveUser);

        var status = sb.Probe();
        Assert.Equal(HardenState.Hardened, status.State);
        Assert.Equal("strong - 1 hosts, 2 accounts in vault", status.Note);
    }

    [Fact]
    public void Token_with_strong_writes_the_slot_for_hostname()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var sb = new Sandbox();
        var result = GhStrongHarden.Migrate(sb.Options(token: "tok-x", hostname: sb.Host2));

        Assert.Equal(new[] { sb.Host2 }, result.Migrated);
        Assert.Equal("tok-x", sb.Vaulted("", sb.Host2));
        Assert.True(new ToolPinStore(sb.Root).TryGet("gh")!.IsStrong);

        var ex = Assert.Throws<InvalidOperationException>(() => GhStrongHarden.Migrate(sb.Options(token: "tok-bad", hostname: sb.Host2)));
        Assert.Contains("auth status", ex.Message);
        Assert.Equal("tok-x", sb.Vaulted("", sb.Host2));
    }

    [Fact]
    public void Probe_reports_Degraded_on_stock_return_token_line_and_missing_active_token()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var sb = new Sandbox();
        File.WriteAllText(sb.HostsPath, $"{sb.Host1}:\n    users:\n        alice:\n    user: alice\n");
        sb.WriteStock(sb.Host1, "alice", "tok-alice");
        GhStrongHarden.Migrate(sb.Options());
        Assert.Equal(HardenState.Hardened, sb.Probe().State);

        sb.WriteStock(sb.Host1, "alice", "tok-alice");
        Assert.Equal(HardenedToolStatus.GhStockReturned, sb.Probe().Reason);
        GhStrongHarden.Migrate(sb.Options());
        Assert.Equal(HardenState.Hardened, sb.Probe().State);

        File.AppendAllText(sb.HostsPath, "    oauth_token: tok-alice\n");
        Assert.Equal(HardenedToolStatus.GhStockReturned, sb.Probe().Reason);
        GhStrongHarden.Migrate(sb.Options());
        Assert.Equal(HardenState.Hardened, sb.Probe().State);
        Assert.DoesNotContain("oauth_token", File.ReadAllText(sb.HostsPath));

        File.WriteAllText(sb.HostsPath, $"{sb.Host1}:\n    users:\n        alice:\n        zed:\n    user: zed\n");
        var status = sb.Probe();
        Assert.Equal(HardenState.Degraded, status.State);
        Assert.Equal($"{HardenedToolStatus.GhNoVaultTokenPrefix}{sb.Host1}; run gh auth login", status.Reason);
    }

    [Fact]
    public void Unharden_writes_stock_entries_back_and_keeps_the_compat_token()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var sb = new Sandbox();
        File.WriteAllText(sb.HostsPath, $"{sb.Host1}:\n    users:\n        alice:\n        bob:\n    user: bob\n");
        sb.WriteStock(sb.Host1, "", "tok-alice");
        sb.WriteStock(sb.Host1, "alice", "tok-alice");
        sb.WriteStock(sb.Host1, "bob", "tok-bob");
        GhStrongHarden.Migrate(sb.Options());
        var compat = VaultNames.ProductPrefix + "secret/GH_TOKEN_" + sb.Suffix.Replace('.', '_');
        sb.Vault.SaveTarget(compat, null, "compat"u8, comment: "");
        try
        {
            var result = GhStrongHarden.Unharden(sb.Options());

            Assert.True(result.WasStrong);
            Assert.True(result.PinRemoved);
            Assert.True(result.ShimRemoved);
            Assert.Equal(3, result.Restored.Count);
            Assert.Empty(sb.Store.Keys());
            var slot = sb.Vault.ReadTarget(sb.StockPrefix + sb.Host1 + ":")!;
            Assert.Equal("tok-bob", Encoding.UTF8.GetString(slot.Blob));
            Assert.Equal("tok-alice", Encoding.UTF8.GetString(sb.Vault.ReadTarget(sb.StockPrefix + sb.Host1 + ":alice")!.Blob));
            Assert.NotNull(sb.Vault.ReadTarget(compat));
            Assert.Null(new ToolPinStore(sb.Root).TryGet("gh"));
            Assert.Equal(HardenState.NotHardened, sb.Probe().State);
        }
        finally
        {
            sb.Vault.DeleteTarget(compat);
        }
    }
}
