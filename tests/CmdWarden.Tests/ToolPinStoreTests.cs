using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Seam: ToolPinStore - store/load/check pins under product root (issue #31).
/// </summary>
public class ToolPinStoreTests
{
    [Fact]
    public void Save_and_TryGet_round_trip()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-pin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var target = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            var store = new ToolPinStore(root);
            store.Save("gh", target);

            var pin = store.TryGet("gh");
            Assert.NotNull(pin);
            Assert.Equal("gh", pin!.Tool);
            Assert.Equal(Path.GetFullPath(target), pin.Path, ignoreCase: true);
            Assert.Equal(ToolPinStore.ComputeSha256Hex(target), pin.Sha256, ignoreCase: true);
            Assert.Equal(ToolPinStore.TryReadSignerThumbprint(target), pin.SignerThumbprint);

            var check = store.Check("gh");
            Assert.True(check.IsOk);
            Assert.NotNull(check.Pin);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Check_returns_Missing_when_no_pin_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-pin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new ToolPinStore(root);
            var check = store.Check("gh");
            Assert.True(check.IsMissing);
            Assert.False(check.IsOk);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Check_returns_Mismatch_when_hash_wrong()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-pin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var target = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            var store = new ToolPinStore(root);
            store.Save("gh", target, sha256Hex: new string('0', 64));

            var check = store.Check("gh");
            Assert.False(check.IsOk);
            Assert.False(check.IsMissing);
            Assert.Contains("hash", check.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void DetectRepin_is_true_on_first_observation_then_true_again_after_a_new_pin()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-pin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new ToolPinStore(root);
            store.Save("gh", Path.Combine(Environment.SystemDirectory, "cmd.exe"));

            // First sighting still counts: a grant taken out before this tool had any pin
            // (PinMissing) must still drop once the harden that first pins it lands.
            Assert.True(store.DetectRepin("gh"));
            Assert.False(store.DetectRepin("gh"));

            store.Save("gh", Path.Combine(Environment.SystemDirectory, "notepad.exe"));

            Assert.True(store.DetectRepin("gh"));
            Assert.False(store.DetectRepin("gh"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void DetectRepin_is_false_when_no_pin_exists_yet()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-pin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new ToolPinStore(root);
            Assert.False(store.DetectRepin("gh"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Check_returns_Mismatch_when_pinned_file_missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-pin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var tempBin = Path.Combine(root, "fake-gh.exe");
            File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), tempBin);
            var store = new ToolPinStore(root);
            store.Save("gh", tempBin);
            File.Delete(tempBin);

            var check = store.Check("gh");
            Assert.False(check.IsOk);
            Assert.False(check.IsMissing);
            Assert.Contains("does not exist", check.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }
}
