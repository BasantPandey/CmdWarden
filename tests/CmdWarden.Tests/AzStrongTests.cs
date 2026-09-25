using System.Text;
using CmdWarden.Agent.Approval;
using CmdWarden.Cli;
using CmdWarden.Cli.Harden;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>#26: strong az moves the az login into the CmdWarden store and back.</summary>
public class AzStrongStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cw-az-" + Guid.NewGuid().ToString("N"));
    private string Stock => Path.Combine(_dir, "stock");
    private string Root => Path.Combine(_dir, "prod");

    public AzStrongStoreTests()
    {
        Directory.CreateDirectory(Stock);
        File.WriteAllText(Path.Combine(Stock, "azureProfile.json"), """{"subscriptions":[{"id":"sub-1"}]}""");
        File.WriteAllBytes(Path.Combine(Stock, "msal_token_cache.bin"), [1, 2, 3, 4]);
        File.WriteAllText(Path.Combine(Stock, "config"), "[core]\noutput = table\n");
        Directory.CreateDirectory(Path.Combine(Stock, "cliextensions"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Harden_run_capture_and_unharden_round_trip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var store = new AzStrongStore(Root, Stock);

        var moved = store.Migrate();
        Assert.Equal(["azureProfile.json", "msal_token_cache.bin"], moved.Order());
        Assert.False(store.StockHasLogin());
        Assert.True(File.Exists(Path.Combine(Stock, "config")));
        Assert.DoesNotContain("sub-1", Encoding.UTF8.GetString(File.ReadAllBytes(store.StorePath)));

        var start = ApprovalMemory.ProcessStartUtc(Environment.ProcessId)!.Value;
        var run = store.Materialize(Environment.ProcessId, start);
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(Path.Combine(run, "msal_token_cache.bin")));
        Assert.True(File.Exists(Path.Combine(run, "config")));
        File.WriteAllBytes(Path.Combine(run, "msal_token_cache.bin"), [9, 9]);

        store.Capture(run);
        Assert.False(Directory.Exists(run));
        Assert.Equal([9, 9], store.Load()["msal_token_cache.bin"]);

        var restored = store.WriteBack();
        Assert.Equal(2, restored.Count);
        Assert.Equal([9, 9], File.ReadAllBytes(Path.Combine(Stock, "msal_token_cache.bin")));
        Assert.Contains("sub-1", File.ReadAllText(Path.Combine(Stock, "azureProfile.json")));
        Assert.False(store.Exists);
    }

    [Fact]
    public void Logout_in_a_run_removes_the_login_from_the_store()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var store = new AzStrongStore(Root, Stock);
        store.Migrate();
        var run = store.Materialize(Environment.ProcessId, ApprovalMemory.ProcessStartUtc(Environment.ProcessId)!.Value);
        File.Delete(Path.Combine(run, "msal_token_cache.bin"));
        File.Delete(Path.Combine(run, "azureProfile.json"));

        store.Capture(run);

        Assert.DoesNotContain(store.Load().Keys, AzStrongStore.LoginFiles.Contains);
    }

    [Fact]
    public void Sweep_removes_run_folders_of_dead_shims_only()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var store = new AzStrongStore(Root, Stock);
        store.Migrate();
        var live = store.Materialize(Environment.ProcessId, ApprovalMemory.ProcessStartUtc(Environment.ProcessId)!.Value);
        var dead = store.Materialize(999_999, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var junk = Directory.CreateDirectory(Path.Combine(store.RunsDir, "not-a-run")).FullName;

        Assert.Equal(2, store.SweepRuns(ApprovalMemory.ProcessStartUtc));
        Assert.True(Directory.Exists(live));
        Assert.False(Directory.Exists(dead));
        Assert.False(Directory.Exists(junk));
    }

    [Fact]
    public void Cli_harden_marks_the_pin_strong_and_unharden_removes_it()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var pins = new ToolPinStore(Root);
        pins.Save("az", Path.Combine(Environment.SystemDirectory, "cmd.exe"));
        var store = new AzStrongStore(Root, Stock);

        AzStrongHarden.Migrate(Root, store);
        Assert.True(pins.TryGet("az")!.IsStrong);

        var result = AzStrongHarden.Unharden(Root, store);
        Assert.True(result.WasStrong);
        Assert.True(result.PinRemoved);
        Assert.True(store.StockHasLogin());
    }
}

[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class AzStrongProcessTests
{
    [Fact]
    public async Task Allowed_run_gets_a_private_config_dir_that_only_its_shim_can_hand_back()
    {
        if (!OperatingSystem.IsWindows())
            return;
        await using var fx = await ApprovalMemoryFixture.CreateAsync("deny");
        if (fx is null)
            return;
        var stock = Path.Combine(fx.ProductRoot, "stock");
        Directory.CreateDirectory(stock);
        File.WriteAllText(Path.Combine(stock, "azureProfile.json"), """{"subscriptions":[]}""");
        File.WriteAllBytes(Path.Combine(stock, "msal_token_cache.bin"), [5, 6, 7]);
        var fakeAz = Path.Combine(fx.ProductRoot, "az.cmd");
        File.WriteAllText(fakeAz, "@echo off\r\nexit /b 0\r\n");
        var pins = new ToolPinStore(fx.ProductRoot);
        pins.Save("az", fakeAz);
        var store = new AzStrongStore(fx.ProductRoot, stock);
        store.Migrate();
        pins.SetMode("az", ToolPin.StrongMode);

        var grant = await AgentAuthorizeClient.AuthorizeAsync("az", ["account", "show"], pipeName: fx.PipeName);

        Assert.True(grant.Allowed);
        Assert.True(grant.MigrateAfterRun);
        var runDir = grant.Env["AZURE_CONFIG_DIR"];
        Assert.StartsWith(store.RunsDir, runDir, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([5, 6, 7], File.ReadAllBytes(Path.Combine(runDir, "msal_token_cache.bin")));
        Assert.EndsWith("cliextensions", grant.Env["AZURE_EXTENSION_DIR"]);
        Assert.Contains(fx.AuditLines(), l => l.Contains(AzStrongStore.AuditName, StringComparison.Ordinal));

        var other = await Assert.ThrowsAsync<Grpc.Core.RpcException>(() =>
            AgentMigrateClient.MigrateAsync("az", [], fx.PipeName, runDir: Path.Combine(store.RunsDir, "1-1")));
        Assert.Equal(Grpc.Core.StatusCode.PermissionDenied, other.StatusCode);

        File.WriteAllBytes(Path.Combine(runDir, "msal_token_cache.bin"), [8]);
        await AgentMigrateClient.MigrateAsync("az", [], fx.PipeName, runDir: runDir);
        Assert.False(Directory.Exists(runDir));
        Assert.Equal([8], store.Load()["msal_token_cache.bin"]);
    }
}
