using CmdWarden.Agent.Identity;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>Process chain helpers for the credential helper tests (#202, #203).</summary>
internal static class TestChain
{
    /// <summary>
    /// Pin the test host's parent process as docker. The chain rule then sees a pinned, signed
    /// tool directly above the caller. Null when the parent cannot be read.
    /// </summary>
    public static string? PinParentAsDocker(string productRoot)
    {
        var chain = new ProcessChainWalker().Walk(Environment.ProcessId);
        var parent = chain.Count > 1 ? chain[1].Path : null;
        if (parent is null)
            return null;
        new ToolPinStore(productRoot).Save("docker", parent);
        return parent;
    }
}
