using CmdWarden.Contracts;

namespace CmdWarden.Tests;

public class VaultNamesTests
{
    [Fact]
    public void TargetName_uses_CmdWarden_prefix()
    {
        Assert.Equal(VaultNames.ProductPrefix + "secret/MY_TOKEN", VaultNames.TargetName("MY_TOKEN"));
        Assert.Equal(VaultNames.ProductPrefix + "secret/MY_TOKEN", VaultNames.TargetName("+MY_TOKEN"));
    }

    [Fact]
    public void TargetName_rejects_unsafe_characters()
    {
        Assert.Throws<ArgumentException>(() => VaultNames.TargetName("bad name"));
        Assert.Throws<ArgumentException>(() => VaultNames.TargetName("a/b"));
    }

    [Fact]
    public void Test_run_uses_its_own_vault_root_never_the_real_one()
    {
        Assert.Equal("CmdWardenTest/", VaultNames.ProductPrefix);
        Assert.StartsWith("CmdWardenTest/", GhVaultNames.Prefix);
        Assert.StartsWith("CmdWardenTest/", VaultNames.TargetName("GH_TOKEN"));
    }
}
