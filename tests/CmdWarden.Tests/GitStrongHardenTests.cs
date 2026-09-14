using System.Text;
using CmdWarden.Cli.Harden;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// cw harden git --strong and cw unharden git (#207) on a test-only CredMan namespace: the temp
/// global config sets credential.namespace to a unique value, so the real GCM entries stay untouched.
/// The pinned git is the real git.exe; every config call goes to a temp GIT_CONFIG_GLOBAL file.
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class GitStrongHardenTests
{
    private sealed class Sandbox : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "cw-gitstrong-" + Guid.NewGuid().ToString("N"));
        public string Namespace { get; } = "lgt" + Guid.NewGuid().ToString("N")[..8];
        public string HostSuffix { get; } = Guid.NewGuid().ToString("N")[..8] + ".example.test";
        public string GlobalConfig => Path.Combine(Root, "gitconfig");
        public string ShimsDir => Path.Combine(Root, "shims");
        public string HelperExe => Path.Combine(ShimsDir, HelperTools.GitHelperExe);
        public string RealGit { get; }
        public CredentialVault Vault { get; } = new();

        public Sandbox()
        {
            RealGit = GitDiscoverer.FindRealGit(productShimsDir: ShimsDir)
                ?? throw new InvalidOperationException("git.exe is required on PATH for this test.");
            Directory.CreateDirectory(ShimsDir);
            File.WriteAllBytes(Path.Combine(ShimsDir, "git.exe"), [0]);
            File.WriteAllBytes(HelperExe, [0]);
            File.WriteAllText(GlobalConfig, $"[credential]\n\tnamespace = {Namespace}\n\thelper = manager-core\n");
            new ToolPinStore(Root).Save("git", RealGit);
        }

        public GitGlobalConfig Config => new(RealGit, GlobalConfig);

        public GitStrongOptions Options => new() { ProductRoot = Root, GlobalConfigPath = GlobalConfig, HomeDir = Root };

        public string Url(string name, string account = "") =>
            "https://" + (account.Length == 0 ? "" : account + "@") + name + "." + HostSuffix;

        public void WriteLegacy(string url, string user, string secret) =>
            Vault.SaveTarget(Namespace + ":" + url, user, Encoding.Unicode.GetBytes(secret), comment: "");

        public IReadOnlyList<VaultTarget> Legacy() => Vault.ListTargets(Namespace + ":");

        public VaultEntry? Vaulted(string url) => Vault.ReadTarget(GitVaultNames.Target(url));

        public HardenedToolStatus Probe() =>
            HardenedToolStatus.Probe("git", Root, path: [new PathEntry(ShimsDir, "user")], gitGlobalConfigPath: GlobalConfig);

        public void Dispose()
        {
            foreach (var t in Legacy())
                Vault.DeleteTarget(t.Target);
            foreach (var t in Vault.ListTargets(GitVaultNames.Prefix + "https://"))
            {
                if (t.Target.Contains(HostSuffix, StringComparison.OrdinalIgnoreCase))
                    Vault.DeleteTarget(t.Target);
            }
            try { Directory.Delete(Root, recursive: true); } catch { /* ignore */ }
        }
    }

    private static string Utf8(VaultEntry? e) => e is null ? "" : Encoding.UTF8.GetString(e.Blob);

    [Fact]
    public void Migrate_vaults_namespace_entries_deletes_legacy_and_writes_helper_config()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var sb = new Sandbox();
        sb.WriteLegacy(sb.Url("a"), "alice", "tok-a");
        sb.WriteLegacy(sb.Url("a", "bob"), "bob", "tok-b");

        var result = GitStrongHarden.Migrate(sb.Options);

        Assert.Equal(2, result.MigratedKeys.Count);
        Assert.Equal(sb.Namespace, result.Namespace);
        Assert.Equal(new[] { "manager-core" }, result.PreviousHelpers);
        Assert.Empty(sb.Legacy());
        Assert.Equal("tok-a", Utf8(sb.Vaulted(sb.Url("a"))));
        Assert.Equal("alice", sb.Vaulted(sb.Url("a"))!.UserName);
        Assert.Equal("tok-b", Utf8(sb.Vaulted(sb.Url("a", "bob"))));

        var helpers = sb.Config.GetAll(GitGlobalConfig.HelperKey);
        Assert.Equal(new[] { "", GitGlobalConfig.ShPath(sb.HelperExe) }, helpers);

        var pin = new ToolPinStore(sb.Root).TryGet("git")!;
        Assert.True(pin.IsStrong);
        Assert.Equal(new[] { "manager-core" }, pin.Strong!.PreviousHelpers);

        var status = sb.Probe();
        Assert.Equal(HardenState.Hardened, status.State);
        Assert.Equal("strong - 1 git hosts in vault", status.Note);

        // Re-run is idempotent and keeps the first previous value.
        var again = GitStrongHarden.Migrate(sb.Options);
        Assert.Empty(again.MigratedKeys);
        Assert.Equal(new[] { "", GitGlobalConfig.ShPath(sb.HelperExe) }, sb.Config.GetAll(GitGlobalConfig.HelperKey));
        Assert.Equal(new[] { "manager-core" }, new ToolPinStore(sb.Root).TryGet("git")!.Strong!.PreviousHelpers);
    }

    [Fact]
    public void Different_existing_vault_value_fails_whole_and_restores_originals()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var sb = new Sandbox();
        sb.WriteLegacy(sb.Url("a"), "alice", "tok-a");
        sb.WriteLegacy(sb.Url("z"), "zed", "tok-z");
        sb.Vault.SaveTarget(GitVaultNames.Target(sb.Url("z")), "zed", "other"u8, comment: "");

        var ex = Assert.Throws<InvalidOperationException>(() => GitStrongHarden.Migrate(sb.Options));
        Assert.Contains(sb.Url("z"), ex.Message, StringComparison.Ordinal);

        Assert.Equal(2, sb.Legacy().Count);
        Assert.Null(sb.Vaulted(sb.Url("a")));
        Assert.Equal(new[] { "manager-core" }, sb.Config.GetAll(GitGlobalConfig.HelperKey));
        Assert.False(new ToolPinStore(sb.Root).TryGet("git")!.IsStrong);
    }

    [Theory]
    [InlineData("dpapi")]
    [InlineData("plaintext")]
    public void Unsupported_credential_store_fails_closed_before_any_write(string store)
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var sb = new Sandbox();
        sb.WriteLegacy(sb.Url("a"), "alice", "tok-a");
        sb.Config.Add("credential.credentialStore", store);

        var ex = Assert.Throws<InvalidOperationException>(() => GitStrongHarden.Migrate(sb.Options));
        Assert.Contains(store, ex.Message, StringComparison.Ordinal);
        Assert.Single(sb.Legacy());
        Assert.Null(sb.Vaulted(sb.Url("a")));
        Assert.Equal(new[] { "manager-core" }, sb.Config.GetAll(GitGlobalConfig.HelperKey));
    }

    [Fact]
    public void Git_credentials_file_fails_closed_and_names_the_file()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var sb = new Sandbox();
        var file = Path.Combine(sb.Root, ".git-credentials");
        File.WriteAllText(file, "https://x:y@example.test\n");
        sb.WriteLegacy(sb.Url("a"), "alice", "tok-a");

        var ex = Assert.Throws<InvalidOperationException>(() => GitStrongHarden.Migrate(sb.Options));
        Assert.Contains(file, ex.Message, StringComparison.Ordinal);
        Assert.Single(sb.Legacy());
    }

    [Fact]
    public void Gh_host_blocks_are_removed_and_saved_and_unharden_restores_everything()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var sb = new Sandbox();
        var ghValue = "!'C:\\Program Files\\GitHub CLI\\gh.exe' auth git-credential";
        var key = "credential.https://" + sb.HostSuffix + ".helper";
        sb.Config.ReplaceAll(key, "");
        sb.Config.Add(key, ghValue);
        sb.Config.Add("credential.https://other." + sb.HostSuffix + ".helper", "manager");
        sb.WriteLegacy(sb.Url("a"), "alice", "tok-a");

        var result = GitStrongHarden.Migrate(sb.Options);

        Assert.Equal(new[] { "", ghValue }, result.RemovedGhBlocks.Select(b => b.Value));
        Assert.Empty(sb.Config.GetAll(key));
        Assert.Equal(new[] { "manager" }, sb.Config.GetAll("credential.https://other." + sb.HostSuffix + ".helper"));
        var pin = new ToolPinStore(sb.Root).TryGet("git")!;
        Assert.Equal(2, pin.Strong!.GhHelperBlocks!.Count);

        var unharden = GitStrongHarden.Unharden(sb.Options);

        Assert.Equal(new[] { sb.Url("a") }, unharden.RestoredKeys);
        Assert.Equal(new[] { "manager-core" }, unharden.RestoredHelpers);
        Assert.True(unharden.PinRemoved);
        Assert.True(unharden.ShimRemoved);
        Assert.True(unharden.HelperRemoved);
        Assert.Equal(new[] { "manager-core" }, sb.Config.GetAll(GitGlobalConfig.HelperKey));
        Assert.Equal(new[] { "", ghValue }, sb.Config.GetAll(key));
        Assert.Null(sb.Vaulted(sb.Url("a")));
        var legacy = sb.Vault.ReadTarget(sb.Namespace + ":" + sb.Url("a"))!;
        Assert.Equal("alice", legacy.UserName);
        Assert.Equal("tok-a", Encoding.Unicode.GetString(legacy.Blob));
        Assert.Null(new ToolPinStore(sb.Root).TryGet("git"));
    }

    [Fact]
    public void Probe_reports_Degraded_on_drift_and_reharden_repairs()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var sb = new Sandbox();
        sb.WriteLegacy(sb.Url("a"), "alice", "tok-a");
        GitStrongHarden.Migrate(sb.Options);
        Assert.Equal(HardenState.Hardened, sb.Probe().State);

        sb.Config.Add(GitGlobalConfig.HelperKey, "manager");
        Assert.Equal(HardenedToolStatus.GitHelperDrift, sb.Probe().Reason);
        GitStrongHarden.Migrate(sb.Options);
        Assert.Equal(HardenState.Hardened, sb.Probe().State);

        var key = "credential.https://" + sb.HostSuffix + ".helper";
        sb.Config.Add(key, "!gh auth git-credential");
        Assert.Equal(HardenedToolStatus.GitGhBlockReturned, sb.Probe().Reason);
        GitStrongHarden.Migrate(sb.Options);
        Assert.Equal(HardenState.Hardened, sb.Probe().State);

        sb.WriteLegacy(sb.Url("b"), "bea", "tok-b");
        Assert.Equal(HardenedToolStatus.GitLegacyReturned, sb.Probe().Reason);
        GitStrongHarden.Migrate(sb.Options);
        Assert.Equal(HardenState.Hardened, sb.Probe().State);
        Assert.Equal("tok-b", Utf8(sb.Vaulted(sb.Url("b"))));
    }

    [Fact]
    public void ShPath_escapes_spaces_and_parens_for_sh()
    {
        Assert.Equal(
            @"C:/Program\ Files\ \(x86\)/CmdWarden/shims/git-credential-cmdwarden.exe",
            GitGlobalConfig.ShPath(@"C:\Program Files (x86)\CmdWarden\shims\git-credential-cmdwarden.exe"));
    }

    [Theory]
    [InlineData("!'C:\\Program Files\\GitHub CLI\\gh.exe' auth git-credential", true)]
    [InlineData("!/usr/bin/gh auth git-credential", true)]
    [InlineData("!gh auth git-credential", true)]
    [InlineData("manager-core", false)]
    [InlineData("!C:/tools/other.exe auth git-credential", false)]
    [InlineData("", false)]
    public void IsGhHelperValue_follows_the_gh_IsOurs_rule(string value, bool expected) =>
        Assert.Equal(expected, GitGlobalConfig.IsGhHelperValue(value));

    [Fact]
    public void Config_env_keys_classify_like_dash_c()
    {
        Assert.True(GitCommandClassifier.HasSecretAdjacentConfigEnv(
            [new("GIT_CONFIG_KEY_0", "credential.helper")]));
        Assert.True(GitCommandClassifier.HasSecretAdjacentConfigEnv(
            [new("GIT_CONFIG_KEY_3", "core.askPass")]));
        Assert.False(GitCommandClassifier.HasSecretAdjacentConfigEnv(
            [new("GIT_CONFIG_KEY_0", "user.name"), new("GIT_CONFIG_VALUE_0", "credential.helper")]));
    }
}
