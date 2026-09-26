using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Seam: HardenedToolStatus probe over a temp product root with injected user PATH (issue #116).
/// </summary>
public class HardenedToolStatusTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cw-hts-" + Guid.NewGuid().ToString("N"));
    private readonly string _real;

    public HardenedToolStatusTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "shims"));
        _real = Path.Combine(_root, "real-gh.exe");
        File.WriteAllText(_real, "real tool bytes");
    }

    private string ShimsDir => Path.Combine(_root, "shims");

    private void Pin() => new ToolPinStore(_root).Save("gh", _real);

    private void Shim() => File.WriteAllText(Path.Combine(ShimsDir, "gh.exe"), "shim");

    [Fact]
    public void Nothing_present_is_NotHardened()
    {
        var s = HardenedToolStatus.Probe("gh", _root, path: HardenedToolStatus.ComposePath("", ""));
        Assert.Equal(HardenState.NotHardened, s.State);
        Assert.Null(s.PinnedPath);
        Assert.Null(s.Reason);
    }

    [Fact]
    public void Pin_shim_and_path_is_Hardened()
    {
        Pin(); Shim();
        var s = HardenedToolStatus.Probe("gh", _root, path: HardenedToolStatus.ComposePath("", @"C:\other;" + ShimsDir + @"\"));
        Assert.Equal(HardenState.Hardened, s.State);
        Assert.Equal(Path.GetFullPath(_real), s.PinnedPath, ignoreCase: true);
        Assert.Null(s.Reason);
    }

    [Fact]
    public void Hash_mismatch_is_Degraded()
    {
        Pin(); Shim();
        File.WriteAllText(_real, "tampered");
        var s = HardenedToolStatus.Probe("gh", _root, path: HardenedToolStatus.ComposePath("", ShimsDir));
        Assert.Equal(HardenState.Degraded, s.State);
        Assert.Contains("hash", s.Reason);
        Assert.NotNull(s.PinnedPath);
    }

    [Fact]
    public void Pinned_path_gone_is_Degraded()
    {
        Pin(); Shim();
        File.Delete(_real);
        var s = HardenedToolStatus.Probe("gh", _root, path: HardenedToolStatus.ComposePath("", ShimsDir));
        Assert.Equal(HardenState.Degraded, s.State);
        Assert.Contains("does not exist", s.Reason);
    }

    [Fact]
    public void Shim_present_but_not_on_path_is_Degraded()
    {
        Pin(); Shim();
        var s = HardenedToolStatus.Probe("gh", _root, path: HardenedToolStatus.ComposePath("", @"C:\other"));
        Assert.Equal(HardenState.Degraded, s.State);
        Assert.Contains("PATH", s.Reason);
    }

    [Fact]
    public void Shim_without_pin_is_Degraded()
    {
        Shim();
        var s = HardenedToolStatus.Probe("gh", _root, path: HardenedToolStatus.ComposePath("", ShimsDir));
        Assert.Equal(HardenState.Degraded, s.State);
        Assert.Contains("pin", s.Reason);
    }

    private string VendorDir(string name, string? toolFile = null)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        if (toolFile is not null)
            File.WriteAllText(Path.Combine(dir, toolFile), "vendor");
        return dir;
    }

    [Fact]
    public void Machine_entry_with_tool_before_shims_is_Degraded_with_machine_scope()
    {
        Pin(); Shim();
        var vendor = VendorDir("vendor", "gh.exe");
        var s = HardenedToolStatus.Probe("gh", _root, path: HardenedToolStatus.ComposePath(vendor, ShimsDir));
        Assert.Equal(HardenState.Degraded, s.State);
        Assert.Equal($"shim not first on PATH: {vendor} (machine) precedes shims; run cw doctor --fix-path", s.Reason);
    }

    [Fact]
    public void User_entry_with_tool_before_shims_is_Degraded_with_user_scope()
    {
        Pin(); Shim();
        var vendor = VendorDir("vendor", "gh.cmd");
        var s = HardenedToolStatus.Probe("gh", _root, path: HardenedToolStatus.ComposePath("", vendor + ";" + ShimsDir));
        Assert.Equal(HardenState.Degraded, s.State);
        Assert.Contains($"{vendor} (user) precedes shims", s.Reason);
    }

    [Fact]
    public void Earlier_entry_without_tool_file_is_Hardened()
    {
        Pin(); Shim();
        var vendor = VendorDir("vendor", "other.exe");
        var s = HardenedToolStatus.Probe("gh", _root, path: HardenedToolStatus.ComposePath(vendor, ShimsDir));
        Assert.Equal(HardenState.Hardened, s.State);
        Assert.Null(s.Reason);
    }

    [Fact]
    public void Tool_after_shims_is_Hardened()
    {
        Pin(); Shim();
        var vendor = VendorDir("vendor", "gh.exe");
        var s = HardenedToolStatus.Probe("gh", _root, path: HardenedToolStatus.ComposePath("", ShimsDir + ";" + vendor));
        Assert.Equal(HardenState.Hardened, s.State);
    }

    [Fact]
    public void Process_path_is_stale_when_registry_entries_are_missing_or_reordered()
    {
        var registry = HardenedToolStatus.ComposePath(@"C:\m1;C:\m2", @"C:\u1");
        Assert.False(HardenedToolStatus.IsProcessPathStale(registry, @"C:\extra;C:\m1;C:\m2;C:\u1\"));
        Assert.True(HardenedToolStatus.IsProcessPathStale(registry, @"C:\m1;C:\m2"));
        Assert.True(HardenedToolStatus.IsProcessPathStale(registry, @"C:\m2;C:\m1;C:\u1"));
    }

    [Fact]
    public void Catalog_is_the_first_four_then_ssh_in_order()
    {
        Assert.Equal(["gh", "git", "az", "docker", "ssh"], ToolCatalog.Tools.Select(t => t.Id));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
