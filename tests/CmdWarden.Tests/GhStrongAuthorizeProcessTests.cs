using System.Diagnostics;
using System.Text;
using CmdWarden.Agent.Approval;
using CmdWarden.Cli;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Grpc;

namespace CmdWarden.Tests;

/// <summary>
/// Strong gh through the Agent (#208): host-class injection, per-entry audit rows, compat GH_TOKEN
/// ignored, MigrateToolStore gate and post-run call. The Agent runs with a private stock namespace
/// (CW_GH_STOCK_PREFIX) and a temp GH_CONFIG_DIR; vault entries use CmdWarden/gh/ and are removed after.
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class GhStrongAuthorizeProcessTests
{
    private const string Ghes1 = "ghes-one.example.test";
    private const string Ghes2 = "ghes-two.example.test";

    [Fact]
    public async Task Strong_injects_by_host_class_and_audits_one_row_per_entry()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await Fixture.CreateAsync();
        if (fx is null)
            return;
        fx.PinCmdAsGh(strong: true);
        await fx.SaveCompatTokenAsync("ghp_compat");

        // github.com only.
        fx.Store.Save("", "github.com", "tok-dotcom"u8);
        var grant = await fx.AuthorizeAsync(new[] { "pr", "list" });
        Assert.Equal("tok-dotcom", grant.Env["GH_TOKEN"]);
        Assert.False(grant.Env.ContainsKey("GH_ENTERPRISE_TOKEN"));
        Assert.False(grant.MigrateAfterRun);
        var audit = fx.ReadAudit();
        Assert.Contains("CmdWarden/gh/github.com", audit);
        Assert.DoesNotContain("tok-dotcom", audit);
        Assert.DoesNotContain("\"GH_TOKEN\"", audit);

        // One GHES host: injected without a named host.
        fx.Store.Save("", Ghes1, "tok-one"u8);
        grant = await fx.AuthorizeAsync(new[] { "pr", "list" });
        Assert.Equal("tok-one", grant.Env["GH_ENTERPRISE_TOKEN"]);
        Assert.Contains("CmdWarden/gh/" + Ghes1, fx.ReadAudit());

        // Two GHES hosts: none without a name; the named one with --hostname, -R, or GH_HOST.
        fx.Store.Save("", Ghes2, "tok-two"u8);
        grant = await fx.AuthorizeAsync(new[] { "pr", "list" });
        Assert.Equal("tok-dotcom", grant.Env["GH_TOKEN"]);
        Assert.False(grant.Env.ContainsKey("GH_ENTERPRISE_TOKEN"));
        grant = await fx.AuthorizeAsync(new[] { "pr", "list", "--hostname", Ghes2 });
        Assert.Equal("tok-two", grant.Env["GH_ENTERPRISE_TOKEN"]);
        grant = await fx.AuthorizeAsync(new[] { "pr", "list", "-R", Ghes1 + "/org/repo" });
        Assert.Equal("tok-one", grant.Env["GH_ENTERPRISE_TOKEN"]);
        grant = await fx.AuthorizeAsync(new[] { "pr", "list" }, new Dictionary<string, string> { ["GH_HOST"] = Ghes2 });
        Assert.Equal("tok-two", grant.Env["GH_ENTERPRISE_TOKEN"]);

        // Keyring auth mutations carry no token; login/refresh/logout ask for the post-run migrate.
        grant = await fx.AuthorizeAsync(new[] { "auth", "login" });
        Assert.False(grant.Env.ContainsKey("GH_TOKEN"));
        Assert.True(grant.MigrateAfterRun);
        grant = await fx.AuthorizeAsync(new[] { "auth", "switch" });
        Assert.False(grant.MigrateAfterRun);

        // Compat mode still uses the compat GH_TOKEN and never asks for a migrate.
        new ToolPinStore(fx.ProductRoot).SetMode("gh", null);
        grant = await fx.AuthorizeAsync(new[] { "pr", "list" });
        Assert.Equal("ghp_compat", grant.Env["GH_TOKEN"]);
        grant = await fx.AuthorizeAsync(new[] { "auth", "login" });
        Assert.False(grant.MigrateAfterRun);
    }

    [Fact]
    public async Task MigrateToolStore_is_refused_in_compat_and_runs_after_login_exit_0_only()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await Fixture.CreateAsync();
        if (fx is null)
            return;

        // Fake real gh: auth login exits by %CW_FAKE_EXIT%; auth status verifies every token.
        var realGh = Path.Combine(fx.ProductRoot, "real-gh.cmd");
        await File.WriteAllTextAsync(realGh, """
            @echo off
            if "%1"=="auth" if "%2"=="status" exit /b 0
            exit /b %CW_FAKE_EXIT%
            """);
        new ToolPinStore(fx.ProductRoot).Save("gh", realGh);

        // Compat: refused.
        var ex = await Assert.ThrowsAsync<Grpc.Core.RpcException>(() =>
            AgentMigrateClient.MigrateAsync("gh", new[] { "auth", "login" }, fx.PipeName));
        Assert.Equal(Grpc.Core.StatusCode.FailedPrecondition, ex.StatusCode);

        new ToolPinStore(fx.ProductRoot).SetMode("gh", ToolPin.StrongMode);
        fx.WriteStock("github.com", "", "tok-alice");
        fx.WriteStock("github.com", "alice", "tok-alice");
        await File.WriteAllTextAsync(fx.HostsPath, "github.com:\n    users:\n        alice:\n    user: alice\n");

        // Login exit non-zero: the stock entry stays.
        Environment.SetEnvironmentVariable("CW_FAKE_EXIT", "1");
        Assert.Equal(1, await GhShimApp.RunAsync(new[] { "auth", "login" }, pipeName: fx.PipeName, timeout: TimeSpan.FromSeconds(30)));
        Assert.Equal(2, fx.StockTargets().Count);
        Assert.Empty(fx.Store.Keys());

        // Login exit 0: stock entries move into the vault.
        Environment.SetEnvironmentVariable("CW_FAKE_EXIT", "0");
        Assert.Equal(0, await GhShimApp.RunAsync(new[] { "auth", "login" }, pipeName: fx.PipeName, timeout: TimeSpan.FromSeconds(30)));
        Assert.Empty(fx.StockTargets());
        Assert.Equal("tok-alice", Encoding.UTF8.GetString(fx.Store.Read("", "github.com")!));
        Assert.Equal("tok-alice", Encoding.UTF8.GetString(fx.Store.Read("alice", "github.com")!));
        Assert.Contains("migrate-store", fx.ReadAudit());

        // Logout exit 0: the entry hosts.yml no longer lists goes.
        await File.WriteAllTextAsync(fx.HostsPath, "");
        Assert.Equal(0, await GhShimApp.RunAsync(new[] { "auth", "logout" }, pipeName: fx.PipeName, timeout: TimeSpan.FromSeconds(30)));
        Assert.Empty(fx.Store.Keys());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public string PipeName { get; }
        public string ProductRoot { get; }
        public string PolicyPath { get; }
        public string StockPrefix { get; }
        public string HostsPath => Path.Combine(ProductRoot, "ghconfig", "hosts.yml");
        public GhStrongStore Store { get; }
        private readonly Process _agent;

        private Fixture(string pipeName, string productRoot, string policyPath, string stockPrefix, Process agent)
        {
            PipeName = pipeName;
            ProductRoot = productRoot;
            PolicyPath = policyPath;
            StockPrefix = stockPrefix;
            _agent = agent;
            Store = new GhStrongStore(null, null, stockPrefix, HostsPath);
        }

        public static async Task<Fixture?> CreateAsync()
        {
            // A strong-hardened dev machine owns CmdWarden/gh/github.com; this test must not touch it.
            if (new CredentialVault().ReadTarget(GhVaultNames.Target("github.com")) is not null)
                return null;
            var pipeName = $"{AgentEndpoints.PipeNamePrefix}-ghstrong-{Guid.NewGuid():N}";
            var productRoot = Path.Combine(Path.GetTempPath(), "cw-prod-" + Guid.NewGuid().ToString("N"));
            var policyPath = Path.Combine(productRoot, "policy.json");
            var stockPrefix = "lgt-" + Guid.NewGuid().ToString("N")[..8] + ":";
            Directory.CreateDirectory(Path.Combine(productRoot, "ghconfig"));

            Environment.SetEnvironmentVariable("CW_PIPE_NAME", pipeName);
            Environment.SetEnvironmentVariable("CW_POLICY_PATH", policyPath);
            Environment.SetEnvironmentVariable(ProductPaths.EnvVar, productRoot);
            Environment.SetEnvironmentVariable("GH_TOKEN", null);
            Environment.SetEnvironmentVariable("GH_HOST", null);

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                ArgumentList = { TestPaths.FindAgentDll() },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.Environment["CW_PIPE_NAME"] = pipeName;
            psi.Environment["CW_POLICY_PATH"] = policyPath;
            psi.Environment[ProductPaths.EnvVar] = productRoot;
            psi.Environment[ApprovalGateFactory.EnvVar] = "off";
            psi.Environment["CW_GH_STOCK_PREFIX"] = stockPrefix;
            psi.Environment["GH_CONFIG_DIR"] = Path.Combine(productRoot, "ghconfig");
            var agent = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start agent.");

            var deadline = DateTime.UtcNow.AddSeconds(25);
            while (true)
            {
                if (agent.HasExited)
                    throw new InvalidOperationException("Agent exited: " + await agent.StandardError.ReadToEndAsync());
                try
                {
                    _ = await AgentHealthClient.GetHealthAsync(pipeName, TimeSpan.FromMilliseconds(500));
                    break;
                }
                catch when (DateTime.UtcNow < deadline)
                {
                    await Task.Delay(150);
                }
            }

            var fx = new Fixture(pipeName, productRoot, policyPath, stockPrefix, agent);
            var id = await AgentHealthClient.ResolveIdentityAsync(pipeName);
            if (!id.AutoApproveEligible)
            {
                await fx.DisposeAsync();
                return null;
            }
            var store = new PolicyStore(policyPath);
            store.Load();
            store.Enroll(id.SelectedPolicyKey, LauncherEnrollmentKind.Terminal);
            store.Save();
            return fx;
        }

        public void PinCmdAsGh(bool strong)
        {
            var pins = new ToolPinStore(ProductRoot);
            pins.Save("gh", Path.Combine(Environment.SystemDirectory, "cmd.exe"));
            if (strong)
                pins.SetMode("gh", ToolPin.StrongMode);
        }

        public Task SaveCompatTokenAsync(string value) =>
            AgentVaultClient.SaveAsync(SessionAgentServiceNames.GhToken, Encoding.UTF8.GetBytes(value), PipeName);

        public Task<AuthorizeResponse> AuthorizeAsync(string[] argv, IReadOnlyDictionary<string, string>? callerEnv = null) =>
            AgentAuthorizeClient.AuthorizeAsync("gh", argv, pipeName: PipeName, callerEnv: callerEnv);

        public void WriteStock(string host, string user, string token) =>
            new CredentialVault().SaveTarget(StockPrefix + host + ":" + user, user.Length == 0 ? null : user, Encoding.UTF8.GetBytes(token), comment: "");

        public IReadOnlyList<string> StockTargets() => new CredentialVault().ListTargets(StockPrefix).Select(t => t.Target).ToList();

        public string ReadAudit() =>
            string.Concat(Directory.GetFiles(Path.Combine(ProductRoot, "audit"), "gates-*.ndjson").Select(File.ReadAllText));

        public async ValueTask DisposeAsync()
        {
            var vault = new CredentialVault();
            try { await AgentVaultClient.DeleteAsync(SessionAgentServiceNames.GhToken, PipeName); } catch { /* ignore */ }
            foreach (var t in vault.ListTargets(StockPrefix))
                vault.DeleteTarget(t.Target);
            foreach (var t in vault.ListTargets(GhVaultNames.Prefix))
            {
                var (_, host) = GhVaultNames.Parse(t.Target[GhVaultNames.Prefix.Length..]);
                if (host == "github.com" || host.EndsWith(".example.test", StringComparison.Ordinal))
                    vault.DeleteTarget(t.Target);
            }
            try { if (!_agent.HasExited) _agent.Kill(entireProcessTree: true); } catch { /* ignore */ }
            await _agent.WaitForExitAsync();
            _agent.Dispose();
            Environment.SetEnvironmentVariable("CW_PIPE_NAME", null);
            Environment.SetEnvironmentVariable("CW_POLICY_PATH", null);
            Environment.SetEnvironmentVariable(ProductPaths.EnvVar, null);
            Environment.SetEnvironmentVariable("CW_FAKE_EXIT", null);
            try { Directory.Delete(ProductRoot, recursive: true); } catch { /* ignore */ }
        }
    }
}
