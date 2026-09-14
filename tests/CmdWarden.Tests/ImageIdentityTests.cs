using CmdWarden.Agent.Identity;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

public class ImageIdentityTests
{
    [Fact]
    public void Populate_sets_kind_for_existing_image()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var path = Environment.ProcessPath;
        Assert.False(string.IsNullOrWhiteSpace(path));

        var node = new ProcessNode
        {
            Pid = Environment.ProcessId,
            ParentPid = 0,
            Path = path,
            FileName = Path.GetFileName(path),
        };

        ImageIdentity.Populate(node);
        Assert.True(
            node.Kind is LauncherKinds.Authenticode or LauncherKinds.PathHash,
            $"expected authenticode or pathhash, got {node.Kind}");
        Assert.NotEqual(LauncherKinds.PolicyKeyUnknown, node.PolicyKey);
    }

    [Fact]
    public void Populate_caches_per_image_and_recomputes_when_file_changes()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var path = Path.Combine(Path.GetTempPath(), "cw-img-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllText(path, "not a real pe, v1");
        try
        {
            ProcessNode Node() => new() { Pid = 1, ParentPid = 0, Path = path, FileName = Path.GetFileName(path) };

            var first = Node();
            ImageIdentity.Populate(first);
            Assert.Equal(LauncherKinds.PathHash, first.Kind);

            var again = Node();
            ImageIdentity.Populate(again);
            Assert.Equal(first.Sha256, again.Sha256);
            Assert.Equal(first.PolicyKey, again.PolicyKey);

            File.WriteAllText(path, "not a real pe, v2 (longer)");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
            var changed = Node();
            ImageIdentity.Populate(changed);
            Assert.NotEqual(first.Sha256, changed.Sha256);
            Assert.NotEqual(first.PolicyKey, changed.PolicyKey);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }
}
