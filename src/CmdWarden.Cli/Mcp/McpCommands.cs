using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using CmdWarden.Cli.Hooks;

namespace CmdWarden.Cli.Mcp;

/// <summary><c>cw mcp</c> (#34): serve the CmdWarden MCP tools on stdio, or add the server to a harness.</summary>
public static class McpCommands
{
    public const string ServerName = "cmdwarden";

    public static async Task<int> McpAsync(string[] args)
    {
        switch (args)
        {
            case [] or ["serve"]:
                using (var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false)))
                using (var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)))
                    await McpServer.RunAsync(stdin, stdout, McpTools.All(), McpTools.Instructions).ConfigureAwait(false);
                return 0;
            case ["install", "claude"]:
                return RunClaude(["mcp", "add", "--scope", "user", ServerName, "--", .. ServerCommand()]);
            case ["uninstall", "claude"]:
                return RunClaude(["mcp", "remove", "--scope", "user", ServerName]);
            case ["install", "cursor"]:
                Console.WriteLine($"Cursor MCP server: {(InstallCursor(CursorMcpPath(), ServerCommand()) ? "added" : "already there")} ({CursorMcpPath()})");
                return 0;
            case ["uninstall", "cursor"]:
                Console.WriteLine($"Cursor MCP server: {(UninstallCursor(CursorMcpPath()) ? "removed" : "not there")} ({CursorMcpPath()})");
                return 0;
            case ["install", "codex"]:
                Console.WriteLine($"Codex MCP server: {InstallCodex(CodexConfig.Path(), ServerCommand()) switch
                {
                    CodexConfig.Change.Added => "added",
                    CodexConfig.Change.Updated => "updated",
                    CodexConfig.Change.UserOwned => $"skipped: your own [mcp_servers.{ServerName}] table is there",
                    _ => "already there",
                }} ({CodexConfig.Path()})");
                return 0;
            case ["uninstall", "codex"]:
                Console.WriteLine($"Codex MCP server: {(CodexConfig.Remove(CodexConfig.Path(), CodexBlock) ? "removed" : "not there")} ({CodexConfig.Path()})");
                return 0;
            default:
                Console.WriteLine("Usage: cw mcp [serve] | cw mcp install|uninstall claude|cursor|codex");
                Console.WriteLine("  serve    Run the CmdWarden MCP server on stdio (the harness starts it).");
                Console.WriteLine("  Tools: run_with_secret, list_allowed, why_denied. Secret values never go back to the model.");
                return args.Length > 0 && args[0] is "-h" or "--help" or "help" ? 0 : 1;
        }
    }

    /// <summary>This cw, then "mcp": what the harness starts.</summary>
    public static IReadOnlyList<string> ServerCommand() => [.. HookInstaller.SelfCommandParts(), "mcp"];

    private static int RunClaude(IReadOnlyList<string> args)
    {
        var shown = "claude " + string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
        var psi = new ProcessStartInfo("claude") { UseShellExecute = false };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine($"Claude Code (claude) is not on PATH. Run this when it is: {shown}");
            return 1;
        }
    }

    public const string CodexBlock = "mcp";

    /// <summary>#66: the [mcp_servers.cmdwarden] table in the Codex config.toml.</summary>
    public static CodexConfig.Change InstallCodex(string configPath, IReadOnlyList<string> command) =>
        CodexConfig.Set(configPath, CodexBlock, $"mcp_servers.{ServerName}",
            $"[mcp_servers.{ServerName}]\ncommand = {CodexConfig.Literal(command[0])}\n" +
            $"args = [{string.Join(", ", command.Skip(1).Select(CodexConfig.Literal))}]");

    public static string CursorMcpPath(string? home = null) =>
        Path.Combine(home ?? CmdWarden.Contracts.ProductPaths.UserHome(), ".cursor", "mcp.json");

    /// <summary>Adds mcpServers.cmdwarden to the Cursor config. Other servers stay as they are.</summary>
    public static bool InstallCursor(string path, IReadOnlyList<string> command)
    {
        var root = HookInstaller.Load(path);
        var servers = root["mcpServers"] as JsonObject ?? new JsonObject();
        root["mcpServers"] = servers;
        var entry = new JsonObject
        {
            ["command"] = command[0],
            ["args"] = new JsonArray(command.Skip(1).Select(a => (JsonNode)a).ToArray()),
        };
        if (JsonNode.DeepEquals(servers[ServerName], entry))
            return false;
        servers[ServerName] = entry;
        HookInstaller.Save(path, root);
        return true;
    }

    public static bool UninstallCursor(string path)
    {
        if (!File.Exists(path) || HookInstaller.Load(path)["mcpServers"] is not JsonObject servers || !servers.Remove(ServerName))
            return false;
        HookInstaller.Save(path, servers.Root.AsObject());
        return true;
    }
}
