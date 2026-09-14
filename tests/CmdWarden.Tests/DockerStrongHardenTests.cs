using System.Text;
using System.Text.Json.Nodes;
using CmdWarden.Cli;
using CmdWarden.Cli.Harden;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// cw harden docker --strong and cw unharden docker (#204) on a test-only CredMan namespace:
/// every legacy and inline entry carries a unique URL prefix, so the real Docker store stays untouched.
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class DockerStrongHardenTests
{
    private sealed class Sandbox : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "cw-dockstrong-" + Guid.NewGuid().ToString("N"));
        public string Prefix { get; } = "https://lgtest-" + Guid.NewGuid().ToString("N")[..8] + ".";
        public string ConfigPath => Path.Combine(Root, "docker", "config.json");
        public string ShimsDir => Path.Combine(Root, "shims");
        public CredentialVault Vault { get; } = new();

        public Sandbox()
        {
            Directory.CreateDirectory(ShimsDir);
            File.WriteAllBytes(Path.Combine(ShimsDir, "docker.exe"), [0]);
            File.WriteAllBytes(Path.Combine(ShimsDir, HelperTools.DockerHelperExe), [0]);
            new ToolPinStore(Root).Save("docker", Path.Combine(Environment.SystemDirectory, "cmd.exe"));
        }

        public string Url(string name) => Prefix + name + ".example.test/v1/";

        public DockerStrongOptions Options => new() { ProductRoot = Root, ConfigPath = ConfigPath, UrlPrefix = Prefix };

        public void WriteConfig(JsonObject config)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, config.ToJsonString());
        }

        public void WriteLegacy(string url, string user, string secret) =>
            Vault.WriteWithLabel(url, user, Encoding.UTF8.GetBytes(secret), DockerConfigFile.LegacyLabel);

        public IReadOnlyList<LabeledEntry> Legacy() =>
            Vault.ReadAllWithLabel(DockerConfigFile.LegacyLabel).Where(e => e.Target.StartsWith(Prefix)).ToList();

        public VaultEntry? Vaulted(string url) => Vault.ReadTarget(VaultNames.HelperTargetName("docker", url));

        public void Dispose()
        {
            foreach (var e in Legacy())
                Vault.DeleteTarget(e.Target);
            foreach (var t in Vault.ListTargets(VaultNames.HelperTargetPrefix("docker") + Prefix))
                Vault.DeleteTarget(t.Target);
            try { Directory.Delete(Root, recursive: true); } catch { /* ignore */ }
        }
    }

    private static string Basic(string user, string password) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password));

    [Fact]
    public void Migrate_vaults_legacy_and_inline_entries_erases_legacy_and_writes_config()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var sb = new Sandbox();
        sb.WriteLegacy(sb.Url("a"), "alice", "pw-a");
        sb.WriteConfig(new JsonObject
        {
            ["credsStore"] = "desktop",
            ["credHelpers"] = new JsonObject { ["gcr.io"] = "gcloud" },
            ["auths"] = new JsonObject
            {
                [sb.Url("b")] = new JsonObject { ["auth"] = Basic("bob", "pw-b") },
                [sb.Url("c")] = new JsonObject { ["identitytoken"] = "tok-c" },
            },
        });

        var result = DockerStrongHarden.Migrate(sb.Options);

        Assert.Equal(3, result.MigratedUrls.Count);
        Assert.Equal("desktop", result.PreviousCredsStore);
        Assert.Contains(result.Warnings, w => w.Contains("gcr.io", StringComparison.Ordinal));

        Assert.Equal(("alice", "pw-a"), Read(sb.Vaulted(sb.Url("a"))));
        Assert.Equal(("bob", "pw-b"), Read(sb.Vaulted(sb.Url("b"))));
        Assert.Equal(("<token>", "tok-c"), Read(sb.Vaulted(sb.Url("c"))));
        Assert.Empty(sb.Legacy());

        var config = DockerConfigFile.Read(sb.ConfigPath);
        Assert.Equal("cmdwarden", config["credsStore"]!.GetValue<string>());
        Assert.Equal("", config["auths"]![sb.Url("b")]!["auth"]!.GetValue<string>());
        Assert.Equal("", config["auths"]![sb.Url("c")]!["identitytoken"]!.GetValue<string>());
        Assert.Equal("gcloud", config["credHelpers"]!["gcr.io"]!.GetValue<string>());

        var pin = new ToolPinStore(sb.Root).TryGet("docker")!;
        Assert.True(pin.IsStrong);
        Assert.Equal("desktop", pin.PreviousCredsStore);

        // Re-run is idempotent and keeps the first previous value.
        var again = DockerStrongHarden.Migrate(sb.Options);
        Assert.Empty(again.MigratedUrls.Except(result.MigratedUrls));
        Assert.Equal("desktop", new ToolPinStore(sb.Root).TryGet("docker")!.PreviousCredsStore);
    }

    [Fact]
    public void Different_existing_vault_value_fails_the_whole_harden_and_changes_nothing()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var sb = new Sandbox();
        sb.WriteLegacy(sb.Url("a"), "alice", "pw-a");
        sb.WriteLegacy(sb.Url("z"), "zed", "pw-z");
        sb.Vault.SaveTarget(VaultNames.HelperTargetName("docker", sb.Url("z")), "zed", "other"u8);
        sb.WriteConfig(new JsonObject { ["credsStore"] = "desktop" });

        var ex = Assert.Throws<InvalidOperationException>(() => DockerStrongHarden.Migrate(sb.Options));
        Assert.Contains(sb.Url("z"), ex.Message, StringComparison.Ordinal);

        Assert.Equal(2, sb.Legacy().Count);
        Assert.Null(sb.Vaulted(sb.Url("a")));
        Assert.Equal("desktop", DockerConfigFile.ReadCredsStore(sb.ConfigPath));
        Assert.False(new ToolPinStore(sb.Root).TryGet("docker")!.IsStrong);
    }

    [Fact]
    public void Config_write_failure_restores_legacy_entries_and_removes_vault_copies()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var sb = new Sandbox();
        sb.WriteLegacy(sb.Url("a"), "alice", "pw-a");
        sb.WriteConfig(new JsonObject { ["credsStore"] = "wincred" });
        using var locked = new ReadOnlyDir(Path.GetDirectoryName(sb.ConfigPath)!);

        Assert.ThrowsAny<Exception>(() => DockerStrongHarden.Migrate(sb.Options));

        var legacy = Assert.Single(sb.Legacy());
        Assert.Equal(sb.Url("a"), legacy.Target);
        Assert.Equal("alice", legacy.UserName);
        Assert.Equal("pw-a", Encoding.UTF8.GetString(legacy.Blob));
        Assert.Null(sb.Vaulted(sb.Url("a")));
        Assert.False(new ToolPinStore(sb.Root).TryGet("docker")!.IsStrong);
    }

    [Fact]
    public void Inline_entry_over_the_blob_cap_fails_the_whole_harden()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var sb = new Sandbox();
        sb.WriteLegacy(sb.Url("a"), "alice", "pw-a");
        sb.WriteConfig(new JsonObject
        {
            ["auths"] = new JsonObject { [sb.Url("big")] = new JsonObject { ["identitytoken"] = new string('t', 3000) } },
        });

        var ex = Assert.Throws<InvalidOperationException>(() => DockerStrongHarden.Migrate(sb.Options));
        Assert.Contains("2560", ex.Message, StringComparison.Ordinal);
        Assert.Single(sb.Legacy());
        Assert.Null(sb.Vaulted(sb.Url("a")));
    }

    [Fact]
    public void Foreign_credHelpers_registry_stays_in_the_docker_store_with_a_warning()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var sb = new Sandbox();
        var foreignHost = sb.Prefix.Replace("https://", "") + "gcr.example.test";
        sb.WriteLegacy("https://" + foreignHost + "/", "gcloud-user", "pw-g");
        sb.WriteLegacy(sb.Url("a"), "alice", "pw-a");
        sb.WriteConfig(new JsonObject
        {
            ["credsStore"] = "desktop",
            ["credHelpers"] = new JsonObject { [foreignHost] = "gcloud" },
        });

        var result = DockerStrongHarden.Migrate(sb.Options);

        Assert.Equal(new[] { sb.Url("a") }, result.MigratedUrls);
        var kept = Assert.Single(sb.Legacy());
        Assert.Equal("https://" + foreignHost + "/", kept.Target);
        Assert.Contains(result.Warnings, w => w.Contains(foreignHost, StringComparison.Ordinal) && w.Contains("stays", StringComparison.Ordinal));

        // The probe does not count the foreign entry as a returned legacy credential.
        // Another Docker login on this machine still makes the probe say Degraded, so check only when none exists.
        var others = sb.Vault.ReadAllWithLabel(DockerConfigFile.LegacyLabel).Where(e => !e.Target.StartsWith(sb.Prefix)).ToList();
        if (others.Count == 0)
        {
            var probe = HardenedToolStatus.Probe("docker", sb.Root, HardenedToolStatus.ComposePath("", sb.ShimsDir), sb.ConfigPath);
            Assert.NotEqual(HardenedToolStatus.DockerLegacyReturned, probe.Reason);
        }
    }

    [Fact]
    public void Inline_auth_with_an_empty_password_fails_the_whole_harden_and_names_the_url()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var sb = new Sandbox();
        sb.WriteConfig(new JsonObject
        {
            ["auths"] = new JsonObject { [sb.Url("empty")] = new JsonObject { ["auth"] = Basic("bob", "") } },
        });

        var ex = Assert.Throws<InvalidOperationException>(() => DockerStrongHarden.Migrate(sb.Options));
        Assert.Contains(sb.Url("empty"), ex.Message, StringComparison.Ordinal);
        Assert.False(new ToolPinStore(sb.Root).TryGet("docker")!.IsStrong);
    }

    [Fact]
    public void Unharden_writes_entries_back_restores_credsStore_and_removes_pin_shim_helper()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var sb = new Sandbox();
        sb.WriteLegacy(sb.Url("a"), "alice", "pw-a");
        sb.WriteConfig(new JsonObject { ["credsStore"] = "desktop" });
        DockerStrongHarden.Migrate(sb.Options);
        Assert.Empty(sb.Legacy());

        var result = DockerStrongHarden.Unharden(sb.Options);

        Assert.Equal(new[] { sb.Url("a") }, result.RestoredUrls);
        Assert.Equal("desktop", result.RestoredCredsStore);
        Assert.True(result.PinRemoved);
        Assert.True(result.ShimRemoved);
        Assert.True(result.HelperRemoved);

        var legacy = Assert.Single(sb.Legacy());
        Assert.Equal("alice", legacy.UserName);
        Assert.Equal("pw-a", Encoding.UTF8.GetString(legacy.Blob));
        Assert.Null(sb.Vaulted(sb.Url("a")));
        Assert.Equal("desktop", DockerConfigFile.ReadCredsStore(sb.ConfigPath));
        Assert.Null(new ToolPinStore(sb.Root).TryGet("docker"));
        Assert.False(File.Exists(Path.Combine(sb.ShimsDir, "docker.exe")));
    }

    [Fact]
    public void Unharden_without_a_previous_credsStore_removes_the_key()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var sb = new Sandbox();
        sb.WriteConfig(new JsonObject { ["auths"] = new JsonObject() });
        DockerStrongHarden.Migrate(sb.Options);
        Assert.Equal("cmdwarden", DockerConfigFile.ReadCredsStore(sb.ConfigPath));

        var result = DockerStrongHarden.Unharden(sb.Options);
        Assert.Equal("", result.RestoredCredsStore);
        Assert.Null(DockerConfigFile.ReadCredsStore(sb.ConfigPath));
    }

    [Fact]
    public void Probe_reports_drift_and_returned_legacy_entries_and_the_vault_count()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var sb = new Sandbox();
        sb.WriteConfig(new JsonObject { ["credsStore"] = "desktop" });
        DockerStrongHarden.Migrate(sb.Options);
        var userPath = sb.ShimsDir;

        var healthyOrForeign = HardenedToolStatus.Probe("docker", sb.Root, HardenedToolStatus.ComposePath("", userPath), sb.ConfigPath);
        if (sb.Vault.ReadAllWithLabel(DockerConfigFile.LegacyLabel).Count == 0)
        {
            Assert.Equal(HardenState.Hardened, healthyOrForeign.State);
            Assert.Matches(@"^strong - \d+ registries in vault$", healthyOrForeign.Note);
        }
        else
        {
            // Another Docker login on this machine: the probe must say so.
            Assert.Equal(HardenedToolStatus.DockerLegacyReturned, healthyOrForeign.Reason);
        }

        sb.WriteLegacy(sb.Url("back"), "alice", "pw");
        var returned = HardenedToolStatus.Probe("docker", sb.Root, HardenedToolStatus.ComposePath("", userPath), sb.ConfigPath);
        Assert.Equal(HardenState.Degraded, returned.State);
        Assert.Equal(HardenedToolStatus.DockerLegacyReturned, returned.Reason);

        // Re-harden repairs the returned entry.
        DockerStrongHarden.Migrate(sb.Options);
        Assert.Empty(sb.Legacy());

        sb.WriteConfig(new JsonObject { ["credsStore"] = "desktop" });
        var drift = HardenedToolStatus.Probe("docker", sb.Root, HardenedToolStatus.ComposePath("", userPath), sb.ConfigPath);
        Assert.Equal(HardenState.Degraded, drift.State);
        Assert.Equal(HardenedToolStatus.DockerCredsStoreDrift, drift.Reason);

        DockerStrongHarden.Migrate(sb.Options);
        Assert.Equal("cmdwarden", DockerConfigFile.ReadCredsStore(sb.ConfigPath));
    }

    [Fact]
    public async Task Strong_grant_carries_no_DOCKER_AUTH_CONFIG_and_compat_grant_does()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "off", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        var pins = new ToolPinStore(fx.ProductRoot);
        pins.Save("docker", Path.Combine(Environment.SystemDirectory, "cmd.exe"));
        var authJson = """{"auths":{"https://index.docker.io/v1/":{"auth":"dGVzdDpzZWNyZXQ="}}}""";
        await AgentVaultClient.SaveAsync("DOCKER_AUTH_CONFIG", Encoding.UTF8.GetBytes(authJson), fx.PipeName);
        try
        {
            var compat = await AgentAuthorizeClient.AuthorizeAsync("docker", new[] { "pull", "alpine" }, pipeName: fx.PipeName);
            Assert.Equal(authJson, compat.Env["DOCKER_AUTH_CONFIG"]);

            pins.SetMode("docker", ToolPin.StrongMode, "desktop");
            var strong = await AgentAuthorizeClient.AuthorizeAsync("docker", new[] { "pull", "alpine" }, pipeName: fx.PipeName);
            Assert.True(strong.Allowed);
            Assert.False(strong.Env.ContainsKey("DOCKER_AUTH_CONFIG"));
        }
        finally
        {
            try { await AgentVaultClient.DeleteAsync("DOCKER_AUTH_CONFIG", fx.PipeName); } catch { /* ignore */ }
        }
    }

    private static (string User, string Secret) Read(VaultEntry? entry)
    {
        Assert.NotNull(entry);
        return (entry!.UserName, Encoding.UTF8.GetString(entry.Blob));
    }
}
