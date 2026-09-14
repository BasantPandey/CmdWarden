using System.Text.Json;

namespace CmdWarden.Cli;

/// <summary>
/// Human-readable audit lines without secret values (issue #34).
/// </summary>
public static class AuditFormatter
{
    public static string FormatLine(string ndjsonLine)
    {
        try
        {
            using var doc = JsonDocument.Parse(ndjsonLine);
            var r = doc.RootElement;
            var ts = Get(r, "ts");
            var decision = Get(r, "decision");
            var tool = Get(r, "tool");
            var cmdClass = Get(r, "commandClass");
            var level = Get(r, "policyLevel");
            var key = Get(r, "launcherPolicyKey");
            var reason = Get(r, "reasonCode");
            var secret = Get(r, "secretName");

            var parts = new List<string>();
            if (ts.Length > 0)
                parts.Add(ts);
            if (decision.Length > 0)
                parts.Add(decision);
            if (tool.Length > 0)
                parts.Add($"tool={tool}");
            if (cmdClass.Length > 0)
                parts.Add($"class={cmdClass}");
            if (level.Length > 0)
                parts.Add($"level={level}");
            if (key.Length > 0)
                parts.Add($"launcher={key}");
            if (reason.Length > 0)
                parts.Add($"reason={reason}");
            if (secret.Length > 0)
                parts.Add($"secret={secret}");

            return "  " + string.Join("  ", parts);
        }
        catch
        {
            // Unknown shape: print raw line but never invent secret material.
            return "  " + ndjsonLine.Trim();
        }
    }

    private static string Get(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? ""
            : "";
}
