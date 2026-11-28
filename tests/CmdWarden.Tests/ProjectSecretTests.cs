using CmdWarden.Contracts;

namespace CmdWarden.Tests;

public class ProjectSecretTests
{
    [Fact]
    public void Normalize_keeps_a_drive_root_and_drops_a_trailing_slash()
    {
        Assert.Equal(@"C:\", VaultNames.NormalizeFolder(@"C:\"));
        Assert.Equal(@"C:\repo", VaultNames.NormalizeFolder(@"C:\repo\"));
        Assert.Null(VaultNames.NormalizeFolder("  "));
        Assert.Null(VaultNames.NormalizeFolder("bad\0name"));
        Assert.Equal(@"C:\", VaultNames.ParentFolder(@"C:\repo"));
        Assert.Null(VaultNames.ParentFolder(@"C:\"));
    }

    [Fact]
    public void Target_is_global_or_the_project_folder()
    {
        Assert.Equal(VaultNames.TargetName("TOKEN"), ProjectSecrets.TargetFor("TOKEN", null));
        Assert.Equal(VaultNames.TargetName("TOKEN"), ProjectSecrets.TargetFor("TOKEN", "  "));
        var folder = VaultNames.NormalizeFolder(@"C:\repo\")!;
        Assert.Equal(VaultNames.ProjectTarget("TOKEN", folder), ProjectSecrets.TargetFor("TOKEN", @"C:\repo\"));
        Assert.Throws<ArgumentException>(() => ProjectSecrets.TargetFor("TOKEN", "bad\0"));
        Assert.Throws<ArgumentException>(() => ProjectSecrets.TargetFor("bad name", null));
    }

    [Fact]
    public void Nearest_folder_wins_over_a_parent_and_the_global_value()
    {
        var app = VaultNames.NormalizeFolder(@"C:\repo\app")!;
        var repo = VaultNames.NormalizeFolder(@"C:\repo")!;
        var store = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            [VaultNames.ProjectTarget("TOKEN", app)] = [1],
            [VaultNames.ProjectTarget("TOKEN", repo)] = [2],
            [VaultNames.TargetName("TOKEN")] = [3],
        };

        var hit = ProjectSecrets.Find("TOKEN", @"C:\repo\app\src", Read(store), Named(store));
        Assert.Equal(new byte[] { 1 }, hit!.Value.Bytes);
        Assert.Equal(VaultNames.ProjectTarget("TOKEN", app), hit.Value.Target);
    }

    [Fact]
    public void A_project_value_counts_for_that_folder_only()
    {
        var work = VaultNames.NormalizeFolder(@"C:\work")!;
        var store = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            [VaultNames.ProjectTarget("TOKEN", work)] = [5],
        };

        var hit = ProjectSecrets.Find("TOKEN", @"C:\work\src", Read(store), Named(store));
        Assert.Equal(new byte[] { 5 }, hit!.Value.Bytes);
        Assert.Null(ProjectSecrets.Find("TOKEN", @"C:\other", Read(store), Named(store)));
        Assert.True(ProjectSecrets.Exists("TOKEN", @"C:\work", Copy(store), CopyNamed(store)));
        Assert.False(ProjectSecrets.Exists("TOKEN", @"C:\other", Read(store), Named(store)));
    }

    [Fact]
    public void An_empty_blob_is_not_a_hit()
    {
        var work = VaultNames.NormalizeFolder(@"C:\work")!;
        var store = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            [VaultNames.ProjectTarget("TOKEN", work)] = [],
            [VaultNames.TargetName("TOKEN")] = [9],
        };

        var hit = ProjectSecrets.Find("TOKEN", @"C:\work", Read(store), Named(store));
        Assert.Equal(new byte[] { 9 }, hit!.Value.Bytes);
        Assert.Equal(VaultNames.TargetName("TOKEN"), hit.Value.Target);
    }

    [Fact]
    public void A_bad_path_uses_the_global_value()
    {
        var calls = 0;
        var hit = ProjectSecrets.Find("TOKEN", "bad\0name", _ =>
        {
            calls++;
            return null;
        }, _ => [4]);
        Assert.Equal(0, calls);
        Assert.Equal(new byte[] { 4 }, hit!.Value.Bytes);
        Assert.Null(ProjectSecrets.Find("bad name", @"C:\repo", _ => [1], _ => [1]));
    }

    [Fact]
    public void Project_flag_reads_the_next_word_unless_it_is_another_flag()
    {
        var withFolder = new[] { "NAME", "--project", @"C:\repo" };
        var index = 1;
        Assert.True(ProjectPathOption.TryTake(withFolder, ref index, out var path));
        Assert.Equal(@"C:\repo", path);
        Assert.Equal(2, index);

        var nextFlag = new[] { "NAME", "--project", "--value" };
        index = 1;
        Assert.True(ProjectPathOption.TryTake(nextFlag, ref index, out path));
        Assert.Null(path);
        Assert.Equal(1, index);

        var bare = new[] { "NAME", "--project" };
        index = 1;
        Assert.True(ProjectPathOption.TryTake(bare, ref index, out path));
        Assert.Null(path);

        var other = new[] { "NAME", "--value", "x" };
        index = 1;
        Assert.False(ProjectPathOption.TryTake(other, ref index, out path));
        Assert.Equal(1, index);
    }

    private static Func<string, byte[]?> Read(Dictionary<string, byte[]> store) =>
        target => store.TryGetValue(target, out var blob) ? blob : null;

    private static Func<string, byte[]?> Named(Dictionary<string, byte[]> store) =>
        name => store.TryGetValue(VaultNames.TargetName(name), out var blob) ? blob : null;

    private static Func<string, byte[]?> Copy(Dictionary<string, byte[]> store) =>
        target => store.TryGetValue(target, out var blob) ? blob.ToArray() : null;

    private static Func<string, byte[]?> CopyNamed(Dictionary<string, byte[]> store) =>
        name => store.TryGetValue(VaultNames.TargetName(name), out var blob) ? blob.ToArray() : null;
}
