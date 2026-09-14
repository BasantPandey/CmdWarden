using CmdWarden.Agent.Identity;

namespace CmdWarden.Tests;

public class ProcessChainWalkerTests
{
    [Fact]
    public void Walk_includes_current_process()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var walker = new ProcessChainWalker();
        var chain = walker.Walk(Environment.ProcessId);
        Assert.NotEmpty(chain);
        Assert.Equal(Environment.ProcessId, chain[0].Pid);
        // testhost / dotnet should resolve a path
        Assert.False(string.IsNullOrWhiteSpace(chain[0].Path));
    }
}
