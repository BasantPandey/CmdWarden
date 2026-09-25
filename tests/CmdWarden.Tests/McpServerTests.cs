using System.Text.Json.Nodes;
using CmdWarden.Cli.Mcp;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>#34 MCP protocol and config files, without an Agent.</summary>
public class McpServerTests
{
    private static readonly McpTool[] Tools =
    [
        new("echo", "Echo the text.", new JsonObject { ["type"] = "object" },
            (args, _) => Task.FromResult(new McpToolResult("said " + (string?)args["text"]))),
        new("bad", "Always fails.", new JsonObject { ["type"] = "object" },
            (_, _) => throw new ArgumentException("program is required.")),
    ];

    private static async Task<JsonObject?> Send(string json) => await McpServer.HandleAsync(json, Tools, "use echo");

    [Fact]
    public async Task Initialize_agrees_on_a_version_and_names_the_server()
    {
        var known = (await Send("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26"}}"""))!;
        Assert.Equal(1, (int)known["id"]!);
        Assert.Equal("2025-03-26", (string?)known["result"]!["protocolVersion"]);
        Assert.Equal("cmdwarden", (string?)known["result"]!["serverInfo"]!["name"]);
        Assert.NotNull(known["result"]!["capabilities"]!["tools"]);
        Assert.Equal("use echo", (string?)known["result"]!["instructions"]);

        var future = (await Send("""{"jsonrpc":"2.0","id":"a","method":"initialize","params":{"protocolVersion":"2099-01-01"}}"""))!;
        Assert.Equal("a", (string?)future["id"]);
        Assert.Equal(McpServer.ProtocolVersions[0], (string?)future["result"]!["protocolVersion"]);
    }

    [Fact]
    public async Task Notifications_get_no_reply_and_bad_input_gets_an_error()
    {
        Assert.Null(await Send("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));
        Assert.Equal(-32700, (int)(await Send("not json"))!["error"]!["code"]!);
        Assert.Equal(-32601, (int)(await Send("""{"jsonrpc":"2.0","id":2,"method":"resources/list"}"""))!["error"]!["code"]!);
        Assert.Equal(-32602, (int)(await Send("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"nope"}}"""))!["error"]!["code"]!);
    }

    [Fact]
    public async Task Tools_list_and_call()
    {
        var list = (await Send("""{"jsonrpc":"2.0","id":4,"method":"tools/list"}"""))!["result"]!["tools"]!.AsArray();
        Assert.Equal(["echo", "bad"], list.Select(t => (string?)t!["name"]));

        var ok = (await Send("""{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"echo","arguments":{"text":"hi"}}}"""))!["result"]!;
        Assert.False((bool)ok["isError"]!);
        Assert.Equal("said hi", (string?)ok["content"]![0]!["text"]);

        var bad = (await Send("""{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"bad","arguments":{}}}"""))!["result"]!;
        Assert.True((bool)bad["isError"]!);
        Assert.Equal("program is required.", (string?)bad["content"]![0]!["text"]);
    }

    [Fact]
    public async Task Run_loop_answers_each_line_and_skips_notifications()
    {
        var input = new StringReader("""
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}
            {"jsonrpc":"2.0","method":"notifications/initialized"}

            {"jsonrpc":"2.0","id":2,"method":"ping"}
            """);
        var output = new StringWriter();
        await McpServer.RunAsync(input, output, Tools, "x");
        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal(2, (int)JsonNode.Parse(lines[1])!["id"]!);
    }

    [Fact]
    public void Real_tools_have_the_names_and_schemas_the_issue_asks_for()
    {
        var tools = McpTools.All();
        Assert.Equal(["run_with_secret", "list_allowed", "why_denied"], tools.Select(t => t.Name));
        Assert.Equal("program", (string?)tools[0].InputSchema["required"]![0]);
        Assert.Equal("The person clicked Deny in the approval popup.", McpTools.Explain(PolicyReasonCodes.UserDenied, "gh"));
    }

    [Fact]
    public void Cursor_install_keeps_other_servers_and_runs_once()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cw-mcp-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = McpCommands.CursorMcpPath(dir);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """{"mcpServers":{"other":{"command":"x"}}}""");
            string[] command = ["C:/tools/cw.exe", "mcp"];

            Assert.True(McpCommands.InstallCursor(path, command));
            Assert.False(McpCommands.InstallCursor(path, command));
            var servers = JsonNode.Parse(File.ReadAllText(path))!["mcpServers"]!;
            Assert.Equal("C:/tools/cw.exe", (string?)servers["cmdwarden"]!["command"]);
            Assert.Equal("mcp", (string?)servers["cmdwarden"]!["args"]![0]);

            Assert.True(McpCommands.UninstallCursor(path));
            Assert.False(McpCommands.UninstallCursor(path));
            servers = JsonNode.Parse(File.ReadAllText(path))!["mcpServers"]!;
            Assert.Null(servers["cmdwarden"]);
            Assert.NotNull(servers["other"]);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}
