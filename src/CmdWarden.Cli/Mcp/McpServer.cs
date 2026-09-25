using System.Text.Json;
using System.Text.Json.Nodes;
using CmdWarden.Contracts;

namespace CmdWarden.Cli.Mcp;

/// <summary>One MCP tool: its name, text for the model, input schema, and handler.</summary>
public sealed record McpTool(string Name, string Description, JsonObject InputSchema, Func<JsonObject, CancellationToken, Task<McpToolResult>> Call);

/// <summary>Text that goes back to the model. <see cref="IsError"/> marks a failed call.</summary>
public sealed record McpToolResult(string Text, bool IsError = false);

/// <summary>
/// A stdio MCP server (#34): one JSON-RPC message per line on stdin, one reply per line on stdout.
/// It serves initialize, ping, tools/list, and tools/call. Nothing else may write to stdout.
/// ponytail: one call at a time; a tools/call that waits for the Approval Gate holds the next
/// message. Harnesses send one tool call per server at a time, so this is enough for now.
/// </summary>
public static class McpServer
{
    public static readonly string[] ProtocolVersions = ["2025-06-18", "2025-03-26", "2024-11-05"];

    public static async Task RunAsync(TextReader input, TextWriter output, IReadOnlyList<McpTool> tools, string instructions,
        CancellationToken cancellationToken = default)
    {
        while (await input.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var reply = await HandleAsync(line, tools, instructions, cancellationToken).ConfigureAwait(false);
            if (reply is null)
                continue;
            await output.WriteLineAsync(reply.ToJsonString()).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The reply to one message, or null for a notification.</summary>
    public static async Task<JsonObject?> HandleAsync(string line, IReadOnlyList<McpTool> tools, string instructions,
        CancellationToken cancellationToken = default)
    {
        JsonObject message;
        try
        {
            message = JsonNode.Parse(line) as JsonObject ?? throw new JsonException("not an object");
        }
        catch (JsonException)
        {
            return Error(null, -32700, "Parse error");
        }

        var id = message["id"]?.DeepClone();
        if (id is null)
            return null;
        var parameters = message["params"] as JsonObject ?? new JsonObject();
        switch ((string?)message["method"])
        {
            case "initialize":
                var asked = (string?)parameters["protocolVersion"];
                return Result(id, new JsonObject
                {
                    ["protocolVersion"] = ProtocolVersions.Contains(asked) ? asked : ProtocolVersions[0],
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = "cmdwarden", ["version"] = ProductInfo.Version },
                    ["instructions"] = instructions,
                });
            case "ping":
                return Result(id, new JsonObject());
            case "tools/list":
                return Result(id, new JsonObject
                {
                    ["tools"] = new JsonArray(tools.Select(t => (JsonNode)new JsonObject
                    {
                        ["name"] = t.Name,
                        ["description"] = t.Description,
                        ["inputSchema"] = t.InputSchema.DeepClone(),
                    }).ToArray()),
                });
            case "tools/call":
                var name = (string?)parameters["name"];
                if (tools.FirstOrDefault(t => t.Name == name) is not { } tool)
                    return Error(id, -32602, $"Unknown tool: {name}");
                McpToolResult result;
                try
                {
                    result = await tool.Call(parameters["arguments"] as JsonObject ?? new JsonObject(), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    result = new McpToolResult(ex.Message, IsError: true);
                }
                return Result(id, new JsonObject
                {
                    ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = result.Text }),
                    ["isError"] = result.IsError,
                });
            default:
                return Error(id, -32601, "Method not found");
        }
    }

    private static JsonObject Result(JsonNode id, JsonObject result) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };

    private static JsonObject Error(JsonNode? id, int code, string text) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = text } };
}
