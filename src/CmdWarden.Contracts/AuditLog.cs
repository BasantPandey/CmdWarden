using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CmdWarden.Contracts;

/// <summary>
/// Append-only NDJSON audit under product audit dir (issue #18 / #29 / #38).
/// </summary>
public sealed class AuditLog
{
    /// <summary>Default retention for gate decision files (~30 days per handoff spec).</summary>
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly Regex GateFileName = new(
        @"^gates-(\d{8})\.ndjson$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly string _dir;
    private readonly object _gate = new();

    public AuditLog(string? productRoot = null)
    {
        var root = productRoot ?? ProductPaths.Root();
        _dir = Path.Combine(root, "audit");
    }

    public string DirectoryPath => _dir;

    public void AppendGateDecision(AuditGateRecord record)
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, "gates-" + DateTime.UtcNow.ToString("yyyyMMdd") + ".ndjson");
        var line = JsonSerializer.Serialize(record, JsonOptions);
        lock (_gate)
        {
            File.AppendAllText(file, line + Environment.NewLine);
        }
    }

    /// <summary>
    /// Deletes <c>gates-yyyyMMdd.ndjson</c> files whose date stamp is older than
    /// <paramref name="retention"/> (default ~30 days). Recent files are left intact.
    /// Best-effort: unreadable or locked files are skipped. Does not log file contents.
    /// </summary>
    /// <returns>Number of files successfully removed.</returns>
    public int PruneOlderThan(TimeSpan? retention = null, DateTime? utcNow = null)
    {
        if (!Directory.Exists(_dir))
            return 0;

        var keepWindow = retention ?? DefaultRetention;
        if (keepWindow < TimeSpan.Zero)
            keepWindow = DefaultRetention;

        var now = utcNow ?? DateTime.UtcNow;
        if (now.Kind == DateTimeKind.Local)
            now = now.ToUniversalTime();
        var cutoff = now.Date.AddDays(-keepWindow.TotalDays);

        var removed = 0;
        lock (_gate)
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(_dir, "gates-*.ndjson");
            }
            catch
            {
                return 0;
            }

            foreach (var path in files)
            {
                var name = Path.GetFileName(path);
                var m = GateFileName.Match(name);
                if (!m.Success)
                    continue;

                if (!DateTime.TryParseExact(
                        m.Groups[1].Value,
                        "yyyyMMdd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var fileDate))
                    continue;

                if (fileDate.Date >= cutoff)
                    continue;

                try
                {
                    File.Delete(path);
                    removed++;
                }
                catch
                {
                    // Skip locked/unreadable files; never fail the caller.
                }
            }
        }

        return removed;
    }

    public IReadOnlyList<string> ReadRecentLines(int maxLines = 50)
    {
        if (!Directory.Exists(_dir))
            return Array.Empty<string>();

        var files = Directory.GetFiles(_dir, "gates-*.ndjson")
            .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var lines = new List<string>();
        foreach (var f in files)
        {
            var part = File.ReadAllLines(f);
            for (var i = part.Length - 1; i >= 0 && lines.Count < maxLines; i--)
            {
                if (!string.IsNullOrWhiteSpace(part[i]))
                    lines.Add(part[i]);
            }

            if (lines.Count >= maxLines)
                break;
        }

        lines.Reverse();
        return lines;
    }

    /// <summary>
    /// Newest-first typed records for the Secret Usage tab (issue #117). Lines that do not
    /// parse are skipped and counted. Throws when the audit dir cannot be listed or read.
    /// </summary>
    public AuditTrailPage ReadRecentRecords(int maxLines = 200)
    {
        var lines = ReadRecentLines(maxLines);
        var records = new List<AuditGateRecord>(lines.Count);
        var skipped = 0;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var r = AuditGateRecord.TryParse(lines[i]);
            if (r is null)
                skipped++;
            else
                records.Add(r);
        }

        return new AuditTrailPage(records, skipped);
    }
}

public sealed record AuditTrailPage(IReadOnlyList<AuditGateRecord> Records, int Skipped);

public sealed class AuditGateRecord
{
    [JsonRequired] public string Ts { get; init; } = DateTime.UtcNow.ToString("o");
    [JsonRequired] public string Decision { get; init; } = "";
    public string? ReasonCode { get; init; }
    [JsonRequired] public string Tool { get; init; } = "";
    public string CommandClass { get; init; } = "";
    public string PolicyLevel { get; init; } = "";
    public string LauncherPolicyKey { get; init; } = "";
    public string LauncherKind { get; init; } = "";
    public string EnrollmentKind { get; init; } = "";
    public string? SecretName { get; init; }
    public string? Purpose { get; init; }
    public string? LauncherPath { get; init; }
    public int? ClientPid { get; init; }
    /// <summary>#36: the agent account that made the call, as "PC\name", or null for the person.</summary>
    public string? AgentAccount { get; init; }
    /// <summary>#32: what the AI agent said the command is for. A statement, never proof.</summary>
    public string? AgentReason { get; init; }
    /// <summary>#46: on a session-grant row, how long the grant lasts: "10m", "1h", or "until-exit".</summary>
    public string? GrantLength { get; init; }

    private static readonly JsonSerializerOptions ParseOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Parsed <see cref="Ts"/>, or null when it is not an ISO-8601 timestamp.</summary>
    [JsonIgnore]
    public DateTimeOffset? Timestamp =>
        DateTimeOffset.TryParse(Ts, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t : null;

    /// <summary>
    /// One audit NDJSON line to a typed record. Null when the JSON is malformed or
    /// ts, decision, or tool is missing. Unknown fields are ignored (issue #117).
    /// </summary>
    public static AuditGateRecord? TryParse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;
        AuditGateRecord? r;
        try
        {
            r = JsonSerializer.Deserialize<AuditGateRecord>(line, ParseOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        return r is { Decision.Length: > 0, Tool.Length: > 0, Timestamp: not null } ? r : null;
    }

    /// <summary>"10:04" today, "Yesterday 09:51", else "3 Sep 10:04" in local time.</summary>
    public string LocalTimeLabel(DateTime? nowLocal = null)
    {
        var local = (Timestamp ?? DateTimeOffset.MinValue).ToLocalTime().DateTime;
        var today = (nowLocal ?? DateTime.Now).Date;
        var hm = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (local.Date == today)
            return hm;
        if (local.Date == today.AddDays(-1))
            return "Yesterday " + hm;
        return local.ToString("d MMM ", CultureInfo.InvariantCulture) + hm;
    }
}
