using CmdWarden.Contracts;

namespace CmdWarden.Tests;

public class VaultNamesTests
{
    [Fact]
    public void TargetName_uses_CmdWarden_prefix()
    {
        Assert.Equal("CmdWarden/secret/MY_TOKEN", VaultNames.TargetName("MY_TOKEN"));
        Assert.Equal("CmdWarden/secret/MY_TOKEN", VaultNames.TargetName("+MY_TOKEN"));
    }

    [Fact]
    public void TargetName_rejects_unsafe_characters()
    {
        Assert.Throws<ArgumentException>(() => VaultNames.TargetName("bad name"));
        Assert.Throws<ArgumentException>(() => VaultNames.TargetName("a/b"));
    }
}
