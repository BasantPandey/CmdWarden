using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>Tray toasts from the audit (#43).</summary>
public class TrayAlertFeedTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private static AuditGateRecord Row(int seconds, string reason, string tool = "gh", string key = "k1") => new()
    {
        Ts = Start.AddSeconds(seconds).ToString("o"),
        Decision = GateDecisions.Deny,
        ReasonCode = reason,
        Tool = tool,
        LauncherPolicyKey = key,
        LauncherPath = @"C:\bin\claude.exe",
    };

    [Fact]
    public void Shows_deny_cooldown_and_canary_rows_after_the_start()
    {
        var feed = new TrayAlertFeed(Start);

        var alerts = feed.Next(
            [Row(3, PolicyReasonCodes.CanaryHit, "git"), Row(2, PolicyReasonCodes.UserDenied), Row(1, PolicyReasonCodes.DenyCooldown), Row(-5, PolicyReasonCodes.DenyCooldown, "az")],
            Start.AddSeconds(4));

        Assert.Equal(2, alerts.Count);
        Assert.Contains("claude.exe ran gh again", alerts[0].Text, StringComparison.Ordinal);
        Assert.Contains("canary", alerts[1].Title, StringComparison.Ordinal);
    }

    [Fact]
    public void A_retry_loop_gives_one_toast_per_quiet_window_and_old_rows_never_repeat()
    {
        var feed = new TrayAlertFeed(Start);

        Assert.Single(feed.Next([Row(2, PolicyReasonCodes.DenyCooldown), Row(1, PolicyReasonCodes.DenyCooldown)], Start.AddSeconds(2)));
        Assert.Empty(feed.Next([Row(10, PolicyReasonCodes.DenyCooldown), Row(2, PolicyReasonCodes.DenyCooldown)], Start.AddSeconds(10)));
        Assert.Single(feed.Next([Row(11, PolicyReasonCodes.DenyCooldown, "az")], Start.AddSeconds(11)));
        Assert.Empty(feed.Next([Row(11, PolicyReasonCodes.DenyCooldown, "az")], Start.AddSeconds(90)));
        Assert.Single(feed.Next([Row(95, PolicyReasonCodes.DenyCooldown)], Start.AddSeconds(95)));
    }
}
