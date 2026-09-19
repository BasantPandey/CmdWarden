using System.Text.Json;

namespace CmdWarden.Cli;

/// <summary>
/// Human-readable audit lines without secret values (issue #34).
/// </summary>
public static class AuditFormatter
{
    public sealed record Row(string Ts, string Decision, string Tool, string CommandClass, string Level,
        string LauncherKey, string Reason, string Secret);

    /// <summary>Parse one NDJSON audit line. Returns null when the line is not JSON.</summary>
    public static Row? Parse(string ndjsonLine)
    {
        try
        {
            using var doc = JsonDocument.Parse(ndjsonLine);
            var r = doc.RootElement;
            return new Row(
                Get(r, "ts"), Get(r, "decision"), Get(r, "tool"), Get(r, "commandClass"),
                Get(r, "policyLevel"), Get(r, "launcherPolicyKey"), Get(r, "reasonCode"), Get(r, "secretName"));
        }
        catch
        {
            return null;
        }
    }

    public static string FormatLine(string ndjsonLine)
    {
        // Unknown shape: print raw line but never invent secret material.
        if (Parse(ndjsonLine) is not { } r)
            return "  " + ndjsonLine.Trim();

        var parts = new List<string>();
        if (r.Ts.Length > 0)
            parts.Add(r.Ts);
        if (r.Decision.Length > 0)
            parts.Add(r.Decision);
        if (r.Tool.Length > 0)
            parts.Add($"tool={r.Tool}");
        if (r.CommandClass.Length > 0)
            parts.Add($"class={r.CommandClass}");
        if (r.Level.Length > 0)
            parts.Add($"level={r.Level}");
        if (r.LauncherKey.Length > 0)
            parts.Add($"launcher={r.LauncherKey}");
        if (r.Reason.Length > 0)
            parts.Add($"reason={r.Reason}");
        if (r.Secret.Length > 0)
            parts.Add($"secret={r.Secret}");

        return "  " + string.Join("  ", parts);
    }

    private static string Get(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? ""
            : "";
}
