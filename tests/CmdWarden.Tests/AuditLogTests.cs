using System.Text.Json;
using CmdWarden.Cli;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Audit trail read/format (issue #34).
/// </summary>
public class AuditLogTests
{
    [Fact]
    public void Append_and_ReadRecent_round_trip()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-aud-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var log = new AuditLog(root);
            log.AppendGateDecision(new AuditGateRecord
            {
                Decision = "auto-allow",
                Tool = "gh",
                CommandClass = "read",
                PolicyLevel = "Trusted",
                LauncherPolicyKey = "auth:sha1:abc",
                LauncherKind = "authenticode",
                EnrollmentKind = "terminal",
                SecretName = "GH_TOKEN",
            });

            var lines = log.ReadRecentLines(10);
            Assert.Single(lines);
            Assert.Contains("auto-allow", lines[0], StringComparison.Ordinal);
            Assert.Contains("gh", lines[0], StringComparison.Ordinal);
            Assert.DoesNotContain("ghp_", lines[0], StringComparison.Ordinal);

            var formatted = AuditFormatter.FormatLine(lines[0]);
            Assert.Contains("auto-allow", formatted, StringComparison.Ordinal);
            Assert.Contains("tool=gh", formatted, StringComparison.Ordinal);
            Assert.Contains("class=read", formatted, StringComparison.Ordinal);
            Assert.Contains("launcher=auth:sha1:abc", formatted, StringComparison.Ordinal);
            Assert.DoesNotContain("password", formatted, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void FormatLine_never_adds_secret_values()
    {
        var line = JsonSerializer.Serialize(new
        {
            ts = "2026-01-01T00:00:00Z",
            decision = "deny",
            tool = "gh",
            commandClass = "secret-reveal",
            secretName = "GH_TOKEN",
            reasonCode = "UserDenied",
        });
        var formatted = AuditFormatter.FormatLine(line);
        Assert.Contains("secret=GH_TOKEN", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Prune_removes_files_older_than_retention_keeps_recent()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-aud-prune-" + Guid.NewGuid().ToString("N"));
        var auditDir = Path.Combine(root, "audit");
        Directory.CreateDirectory(auditDir);
        try
        {
            var now = new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);
            // retention 30 days → cutoff 2026-07-03; older deleted, on/after kept
            WriteGateFile(auditDir, "20260601", """{"ts":"2026-06-01T00:00:00Z","decision":"deny","tool":"gh"}""");
            WriteGateFile(auditDir, "20260702", """{"ts":"2026-07-02T00:00:00Z","decision":"deny","tool":"gh"}""");
            WriteGateFile(auditDir, "20260703", """{"ts":"2026-07-03T00:00:00Z","decision":"auto-allow","tool":"gh"}""");
            WriteGateFile(auditDir, "20260802", """{"ts":"2026-08-02T00:00:00Z","decision":"auto-allow","tool":"gh","secretName":"GH_TOKEN"}""");

            var log = new AuditLog(root);
            var removed = log.PruneOlderThan(TimeSpan.FromDays(30), now);

            Assert.Equal(2, removed);
            Assert.False(File.Exists(Path.Combine(auditDir, "gates-20260601.ndjson")));
            Assert.False(File.Exists(Path.Combine(auditDir, "gates-20260702.ndjson")));
            Assert.True(File.Exists(Path.Combine(auditDir, "gates-20260703.ndjson")));
            Assert.True(File.Exists(Path.Combine(auditDir, "gates-20260802.ndjson")));

            var recent = log.ReadRecentLines(10);
            Assert.Contains(recent, l => l.Contains("auto-allow", StringComparison.Ordinal));
            Assert.DoesNotContain(recent, l => l.Contains("ghp_", StringComparison.Ordinal));
            Assert.All(recent, l => Assert.DoesNotContain("password", l, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Prune_is_noop_when_audit_dir_missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-aud-missing-" + Guid.NewGuid().ToString("N"));
        try
        {
            var log = new AuditLog(root);
            var removed = log.PruneOlderThan(TimeSpan.FromDays(30), new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc));
            Assert.Equal(0, removed);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Prune_does_not_block_append_of_new_gate_decision()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-aud-append-" + Guid.NewGuid().ToString("N"));
        var auditDir = Path.Combine(root, "audit");
        Directory.CreateDirectory(auditDir);
        try
        {
            WriteGateFile(auditDir, "20260101", """{"ts":"2026-01-01T00:00:00Z","decision":"deny","tool":"gh"}""");
            var log = new AuditLog(root);
            log.PruneOlderThan(TimeSpan.FromDays(30), new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc));
            log.AppendGateDecision(new AuditGateRecord
            {
                Decision = "auto-allow",
                Tool = "gh",
                CommandClass = "read",
                PolicyLevel = "Trusted",
                LauncherPolicyKey = "auth:sha1:abc",
                LauncherKind = "authenticode",
                EnrollmentKind = "terminal",
                SecretName = "GH_TOKEN",
            });

            var lines = log.ReadRecentLines(5);
            Assert.Contains(lines, l => l.Contains("auto-allow", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, l => l.Contains("ghp_", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    private static void WriteGateFile(string auditDir, string yyyymmdd, string line)
    {
        File.WriteAllText(Path.Combine(auditDir, $"gates-{yyyymmdd}.ndjson"), line + Environment.NewLine);
    }
}
