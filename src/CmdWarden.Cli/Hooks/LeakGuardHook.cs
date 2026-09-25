using System.Text.Json;
using System.Text.Json.Nodes;
using CmdWarden.Contracts;

namespace CmdWarden.Cli.Hooks;

/// <summary>What the Agent said about a list of texts: the same texts with placeholders, and the names.</summary>
public sealed record LeakCheck(IReadOnlyList<string> Texts, IReadOnlyList<string> Names);

/// <summary>
/// Leak guard hooks (#27). Each handler reads the hook JSON of the harness and returns the JSON to
/// print, or null to print nothing. Every string in the tool output goes to the Agent in one call.
/// </summary>
public static class LeakGuardHook
{
    public static string Note(IReadOnlyList<string> names) =>
        $"{ProductInfo.Name} replaced vaulted secret values in this output: {string.Join(", ", names)}. " +
        "The real values are not available to you. Do not try to read them another way.";

    /// <summary>
    /// Claude Code PostToolUse: <c>updatedToolOutput</c> replaces what the model sees. The output keeps
    /// the shape of <c>tool_response</c>; only the strings change.
    /// </summary>
    public static async Task<string?> ClaudePostToolUseAsync(JsonNode input, Func<IReadOnlyList<string>, string, Task<LeakCheck>> check)
    {
        if (input["tool_response"] is not { } response)
            return null;
        var (copy, names) = await RedactAsync(response, check, "claude:" + (string?)input["tool_name"]).ConfigureAwait(false);
        if (names.Count == 0)
            return null;
        return new JsonObject
        {
            ["hookSpecificOutput"] = new JsonObject
            {
                ["hookEventName"] = "PostToolUse",
                ["updatedToolOutput"] = copy,
                ["additionalContext"] = Note(names),
            },
        }.ToJsonString();
    }

    /// <summary>
    /// Cursor hooks. Cursor can block a file read and can replace MCP output only; it cannot change
    /// shell output, so a shell match only adds a note. Every event still reaches the Agent, so a
    /// canary token in any of them raises the alarm.
    /// </summary>
    public static async Task<string?> CursorAsync(JsonNode input, Func<IReadOnlyList<string>, string, Task<LeakCheck>> check)
    {
        var hookEvent = (string?)input["hook_event_name"] ?? "";
        var source = "cursor:" + hookEvent;
        switch (hookEvent)
        {
            case "beforeReadFile":
            {
                var result = await check([(string?)input["content"] ?? ""], source).ConfigureAwait(false);
                if (result.Names.Count == 0)
                    return """{"permission":"allow"}""";
                var file = (string?)input["file_path"] ?? "this file";
                return new JsonObject
                {
                    ["permission"] = "deny",
                    ["user_message"] = $"{ProductInfo.Name} blocked the read of {file}: it holds vaulted secret {string.Join(", ", result.Names)}.",
                }.ToJsonString();
            }
            case "postToolUse":
            {
                var raw = (string?)input["tool_output"] ?? "";
                JsonNode? parsed = null;
                try { parsed = JsonNode.Parse(raw); } catch (JsonException) { /* plain text */ }
                if (parsed is not null)
                {
                    var (redacted, names) = await RedactAsync(parsed, check, source + ":" + (string?)input["tool_name"]).ConfigureAwait(false);
                    return names.Count == 0 ? null : new JsonObject
                    {
                        ["updated_mcp_tool_output"] = redacted,
                        ["additional_context"] = Note(names),
                    }.ToJsonString();
                }
                var result = await check([raw], source).ConfigureAwait(false);
                return result.Names.Count == 0 ? null : new JsonObject { ["additional_context"] = Note(result.Names) }.ToJsonString();
            }
            case "afterShellExecution":
                await check([(string?)input["command"] ?? "", (string?)input["output"] ?? ""], source).ConfigureAwait(false);
                return null;
            default:
                return null;
        }
    }

    /// <summary>A copy of the tree with each vaulted value replaced, and the matched names.</summary>
    public static async Task<(JsonNode Node, IReadOnlyList<string> Names)> RedactAsync(
        JsonNode root, Func<IReadOnlyList<string>, string, Task<LeakCheck>> check, string source)
    {
        // The holder gives a string root a parent, so every string is replaced the same way.
        var holder = new JsonArray(root.DeepClone());
        var slots = new List<(JsonNode? Parent, object Key, string Text)>();
        Collect(holder, null, "", slots);
        if (slots.Count == 0)
            return (root, []);
        var result = await check(slots.Select(s => s.Text).ToList(), source).ConfigureAwait(false);
        if (result.Names.Count == 0)
            return (root, []);
        for (var i = 0; i < slots.Count; i++)
        {
            if (result.Texts[i] == slots[i].Text)
                continue;
            if (slots[i].Parent is JsonObject obj)
                obj[(string)slots[i].Key] = result.Texts[i];
            else if (slots[i].Parent is JsonArray array)
                array[(int)slots[i].Key] = result.Texts[i];
        }
        return (holder[0]!.DeepClone(), result.Names);
    }

    private static void Collect(JsonNode? node, JsonNode? parent, object key, List<(JsonNode?, object, string)> slots)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj.ToList())
                    Collect(child, obj, name, slots);
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                    Collect(array[i], array, i, slots);
                break;
            case JsonValue value when value.TryGetValue<string>(out var text):
                slots.Add((parent, key, text));
                break;
        }
    }
}
