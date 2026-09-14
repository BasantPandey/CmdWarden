using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Seam: audit NDJSON line -> typed gate record for the Secret Usage tab (issue #117).
/// </summary>
public class AuditGateRecordParserTests
{
    [Fact]
    public void Round_trip_of_written_record()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-agr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var log = new AuditLog(root);
            log.AppendGateDecision(new AuditGateRecord
            {
                Decision = "allow-once",
                ReasonCode = PolicyReasonCodes.NeedsApproval,
                Tool = "gh",
                CommandClass = "write",
                PolicyLevel = "Read",
                LauncherPolicyKey = "auth:sha1:abc",
                LauncherKind = "authenticode",
                EnrollmentKind = "terminal",
                SecretName = "GH_TOKEN",
            });

            var r = AuditGateRecord.TryParse(log.ReadRecentLines(1)[0]);
            Assert.NotNull(r);
            Assert.Equal("allow-once", r.Decision);
            Assert.Equal("NeedsApproval", r.ReasonCode);
            Assert.Equal("gh", r.Tool);
            Assert.Equal("write", r.CommandClass);
            Assert.Equal("auth:sha1:abc", r.LauncherPolicyKey);
            Assert.Equal("authenticode", r.LauncherKind);
            Assert.Equal("GH_TOKEN", r.SecretName);
            Assert.NotNull(r.Timestamp);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[1,2]")]
    [InlineData("""{"ts":123,"decision":"deny","tool":"gh"}""")]
    public void Malformed_json_returns_null(string line) =>
        Assert.Null(AuditGateRecord.TryParse(line));

    [Theory]
    [InlineData("""{"decision":"deny","tool":"gh"}""")]
    [InlineData("""{"ts":"2026-01-01T00:00:00Z","tool":"gh"}""")]
    [InlineData("""{"ts":"2026-01-01T00:00:00Z","decision":"deny"}""")]
    [InlineData("""{"ts":"not a time","decision":"deny","tool":"gh"}""")]
    [InlineData("""{"ts":"2026-01-01T00:00:00Z","decision":"","tool":"gh"}""")]
    [InlineData("""{"ts":"2026-01-01T00:00:00Z","decision":"deny","tool":null}""")]
    public void Missing_required_fields_return_null(string line) =>
        Assert.Null(AuditGateRecord.TryParse(line));

    [Fact]
    public void Extra_fields_are_tolerated()
    {
        var line = """{"ts":"2026-01-01T00:00:00Z","decision":"auto-allow","tool":"gh","future":{"x":1},"note":"n"}""";
        var r = AuditGateRecord.TryParse(line);
        Assert.NotNull(r);
        Assert.Equal("auto-allow", r.Decision);
        Assert.Equal("", r.CommandClass);
        Assert.Null(r.SecretName);
    }

    [Fact]
    public void ReadRecentRecords_is_newest_first_and_counts_skipped()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-agr-list-" + Guid.NewGuid().ToString("N"));
        var auditDir = Path.Combine(root, "audit");
        Directory.CreateDirectory(auditDir);
        try
        {
            File.WriteAllLines(Path.Combine(auditDir, "gates-20260801.ndjson"),
            [
                """{"ts":"2026-08-01T01:00:00Z","decision":"deny","tool":"gh"}""",
                "garbage line",
            ]);
            File.WriteAllLines(Path.Combine(auditDir, "gates-20260802.ndjson"),
            [
                """{"ts":"2026-08-02T01:00:00Z","decision":"auto-allow","tool":"git"}""",
                """{"ts":"2026-08-02T02:00:00Z","decision":"allow-once","tool":"az"}""",
            ]);

            var page = new AuditLog(root).ReadRecentRecords(200);
            Assert.Equal(1, page.Skipped);
            Assert.Equal(["az", "git", "gh"], page.Records.Select(r => r.Tool).ToArray());

            var capped = new AuditLog(root).ReadRecentRecords(2);
            Assert.Equal(["az", "git"], capped.Records.Select(r => r.Tool).ToArray());
            Assert.Equal(0, capped.Skipped);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ReadRecentRecords_empty_when_dir_missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-agr-none-" + Guid.NewGuid().ToString("N"));
        var page = new AuditLog(root).ReadRecentRecords();
        Assert.Empty(page.Records);
        Assert.Equal(0, page.Skipped);
    }

    [Fact]
    public void LocalTimeLabel_today_yesterday_else_date()
    {
        var now = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Local);
        static AuditGateRecord At(DateTime local) => new()
        {
            Ts = new DateTimeOffset(local).ToString("o"),
            Decision = "deny",
            Tool = "gh",
        };

        Assert.Equal("10:04", At(now.Date.AddHours(10).AddMinutes(4)).LocalTimeLabel(now));
        Assert.Equal("Yesterday 09:51", At(now.Date.AddDays(-1).AddHours(9).AddMinutes(51)).LocalTimeLabel(now));
        Assert.Equal("28 Aug 23:15", At(new DateTime(2026, 8, 28, 23, 15, 0, DateTimeKind.Local)).LocalTimeLabel(now));
    }
}
