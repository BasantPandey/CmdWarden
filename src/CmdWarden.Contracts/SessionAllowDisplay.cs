using System.Globalization;

namespace CmdWarden.Contracts;

/// <summary>
/// Shared formatting for <c>cw policy sessions</c> and the Secret Gates tab (#134, #135),
/// so the CLI text and the shell rows cannot drift apart.
/// </summary>
public static class SessionAllowDisplay
{
    /// <summary>ISO 8601 round-trip UTC → local "yyyy-MM-dd HH:mm:ss"; the input verbatim if it will not parse.</summary>
    public static string LocalTime(string isoUtc) =>
        DateTimeOffset.TryParse(isoUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)
            ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : isoUtc;
}
