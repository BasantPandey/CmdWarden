namespace CmdWarden.Contracts;

/// <summary>One toast of the tray icon (#43).</summary>
public sealed record TrayAlert(string Title, string Text);

/// <summary>
/// Turns new audit rows into tray toasts (#43): a deny cooldown that blocked a retry, and a
/// canary hit. One toast per launcher, tool, and reason in each <see cref="QuietWindow"/>, so a
/// harness that retries in a loop does not flood the screen.
/// </summary>
public sealed class TrayAlertFeed
{
    public static readonly TimeSpan QuietWindow = TimeSpan.FromMinutes(1);

    private readonly Dictionary<string, DateTimeOffset> _lastShown = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _since;

    /// <param name="start">Rows at or before this time are old; the tray shows only what happens after it starts.</param>
    public TrayAlertFeed(DateTimeOffset start) => _since = start;

    public IReadOnlyList<TrayAlert> Next(IEnumerable<AuditGateRecord> newestFirst, DateTimeOffset now)
    {
        var fresh = newestFirst
            .Where(r => r.Timestamp is { } t && t > _since)
            .OrderBy(r => r.Timestamp)
            .ToList();
        if (fresh.Count > 0)
            _since = fresh[^1].Timestamp!.Value;

        var alerts = new List<TrayAlert>();
        foreach (var r in fresh)
        {
            if (Build(r) is not { } alert)
                continue;
            var key = string.Join('\n', r.ReasonCode, r.LauncherPolicyKey, r.Tool);
            if (_lastShown.TryGetValue(key, out var last) && now - last < QuietWindow)
                continue;
            _lastShown[key] = now;
            alerts.Add(alert);
        }
        return alerts;
    }

    private static TrayAlert? Build(AuditGateRecord r)
    {
        var who = LauncherName(r);
        return r.ReasonCode switch
        {
            PolicyReasonCodes.DenyCooldown => new TrayAlert(
                $"{ProductInfo.Name} blocked a retry",
                $"{who} ran {r.Tool} again after you denied it. {ProductInfo.Name} blocked it with no popup."),
            PolicyReasonCodes.CanaryHit => new TrayAlert(
                $"{ProductInfo.Name}: canary token used",
                $"{who} used a canary token with {r.Tool}. {ProductInfo.Name} blocks {who} until it restarts."),
            _ => null,
        };
    }

    /// <summary>The file name of the launcher, else its policy key.</summary>
    public static string LauncherName(AuditGateRecord r) =>
        string.IsNullOrWhiteSpace(r.LauncherPath) ? r.LauncherPolicyKey : Path.GetFileName(r.LauncherPath);
}
