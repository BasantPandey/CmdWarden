using CmdWarden.Contracts;

namespace CmdWarden.Tests;

public class LauncherKindsTests
{
    [Fact]
    public void Policy_keys_are_stable_format()
    {
        Assert.Equal("auth:sha1:abcdef", LauncherKinds.PolicyKeyAuthenticode("ABCDEF"));
        Assert.Equal("pathhash:sha256:deadbeef", LauncherKinds.PolicyKeyPathHash("DEADBEEF"));
        Assert.Equal("unknown", LauncherKinds.PolicyKeyUnknown);
    }
}
