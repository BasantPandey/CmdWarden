using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Shared time formatting for `cw policy sessions` and the Secret Gates tab (#134, #135).
/// </summary>
public class SessionAllowDisplayTests
{
    [Fact]
    public void RoundTripUtcRendersAsLocalSeconds()
    {
        var utc = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var expected = utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

        Assert.Equal(expected, SessionAllowDisplay.LocalTime(utc.ToString("O")));
    }

    [Fact]
    public void UnparsableInputIsReturnedVerbatim()
    {
        Assert.Equal("", SessionAllowDisplay.LocalTime(""));
        Assert.Equal("not-a-date", SessionAllowDisplay.LocalTime("not-a-date"));
    }
}
