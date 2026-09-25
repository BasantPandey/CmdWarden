using CmdWarden.Contracts;

namespace CmdWarden.Tests;

public class ShimStopTextTests
{
    [Fact]
    public void User_deny_is_one_plain_sentence()
    {
        var line = ShimStopText.TryPlain("gh",
            "UserDenied: user denied authorize for tool='gh' class='write' level='Read' launcher='cursor'.");

        Assert.Equal("CmdWarden: you denied this gh command.", line);
        Assert.DoesNotContain("launcher=", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate_timeout_is_one_plain_sentence()
    {
        var line = ShimStopText.TryPlain("az",
            "ApprovalUnavailable: Approval Gate unavailable; authorize blocked for tool='az' class='write'.");

        Assert.Equal("CmdWarden: the Approval Gate timed out. The az command did not run.", line);
        Assert.DoesNotContain("class=", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Other_stops_keep_their_own_text()
    {
        Assert.Null(ShimStopText.TryPlain("git", "PinMissing: no pin for tool 'git'. Run cw harden git."));
        Assert.Null(ShimStopText.TryPlain("docker", "Session Agent not reachable. Start it with: cw agent start"));
    }
}
