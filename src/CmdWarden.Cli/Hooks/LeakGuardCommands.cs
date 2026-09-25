using System.Text;
using System.Text.Json.Nodes;
using CmdWarden.Cli.Canary;
using CmdWarden.Contracts;

namespace CmdWarden.Cli.Hooks;

/// <summary><c>cw leak-guard</c> (#27) and <c>cw canary</c> (#29).</summary>
public static class LeakGuardCommands
{
    public static async Task<int> LeakGuardAsync(string[] args)
    {
        switch (args)
        {
            case ["claude" or "cursor"]:
                return await RunHookAsync(args[0]).ConfigureAwait(false);
            case ["install", "claude"]:
                Report(HookInstaller.InstallClaude(HookInstaller.ClaudeSettingsPath(), HookInstaller.SelfCommand("leak-guard claude")),
                    "Claude Code PostToolUse hook", HookInstaller.ClaudeSettingsPath(), "added", "already there");
                return 0;
            case ["install", "cursor"]:
                Report(HookInstaller.InstallCursor(HookInstaller.CursorHooksPath(), HookInstaller.SelfCommand("leak-guard cursor")),
                    "Cursor hooks", HookInstaller.CursorHooksPath(), "added", "already there");
                return 0;
            case ["uninstall", "claude"]:
                Report(HookInstaller.UninstallClaude(HookInstaller.ClaudeSettingsPath()),
                    "Claude Code PostToolUse hook", HookInstaller.ClaudeSettingsPath(), "removed", "not there");
                return 0;
            case ["uninstall", "cursor"]:
                Report(HookInstaller.UninstallCursor(HookInstaller.CursorHooksPath()),
                    "Cursor hooks", HookInstaller.CursorHooksPath(), "removed", "not there");
                return 0;
            default:
                Console.WriteLine("Usage: cw leak-guard install|uninstall claude|cursor");
                Console.WriteLine("  Replace vaulted secret values in tool output with [CmdWarden: NAME] before the model sees them.");
                Console.WriteLine("  claude  PostToolUse hook: replaces the value in the output of every tool.");
                Console.WriteLine("  cursor  Blocks a file read that holds a value, replaces MCP output, notes shell output.");
                Console.WriteLine("The harness runs: cw leak-guard claude|cursor  (hook JSON on stdin).");
                return args.Length > 0 && args[0] is "-h" or "--help" or "help" ? 0 : 1;
        }
    }

    private static void Report(bool changed, string what, string path, string yes, string no) =>
        Console.WriteLine($"{what}: {(changed ? yes : no)} ({path})");

    /// <summary>
    /// The hook itself. A leak guard that cannot reach the Session Agent lets the output pass and
    /// says so; it never blocks the harness.
    /// </summary>
    private static async Task<int> RunHookAsync(string harness)
    {
        var raw = await Console.In.ReadToEndAsync().ConfigureAwait(false);
        JsonNode? input;
        try
        {
            input = JsonNode.Parse(raw);
        }
        catch (System.Text.Json.JsonException)
        {
            return 0;
        }
        if (input is null)
            return 0;

        try
        {
            _ = await AgentLifecycle.EnsureRunningAsync().ConfigureAwait(false);
            var output = harness == "claude"
                ? await LeakGuardHook.ClaudePostToolUseAsync(input, CheckAsync).ConfigureAwait(false)
                : await LeakGuardHook.CursorAsync(input, CheckAsync).ConfigureAwait(false);
            if (output is not null)
                Console.Out.Write(output);
        }
        catch (Exception ex)
        {
            var note = $"{ProductInfo.Name} leak guard could not check this output: {AgentFailure(ex)}";
            Console.Out.Write(harness == "claude"
                ? new JsonObject { ["systemMessage"] = note }.ToJsonString()
                : (string?)input["hook_event_name"] == "beforeReadFile"
                    ? new JsonObject { ["permission"] = "allow", ["user_message"] = note }.ToJsonString()
                    : "{}");
        }
        return 0;
    }

    private static string AgentFailure(Exception ex) =>
        AgentHealthClient.IsAgentUnreachable(ex) ? "Session Agent not reachable" : ex.Message;

    private static async Task<LeakCheck> CheckAsync(IReadOnlyList<string> texts, string source)
    {
        var response = await AgentLeakClient.CheckAsync(texts, source, timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        return new LeakCheck(response.Texts, response.Names);
    }

    public static int Canary(string[] args)
    {
        var store = new CanaryStore();
        if (args is ["status"] or [])
        {
            var entries = store.Load();
            if (entries.Count == 0)
            {
                Console.WriteLine("No canary tokens. Add them with: cw canary install [--env <file>]...");
                return 0;
            }
            Ui.Title($"{ProductInfo.Name} canary tokens");
            foreach (var e in entries)
                Ui.Kv(e.Kind, $"{e.Location}  ({string.Join(", ", e.Tokens.Select(t => t.Name))})");
            return 0;
        }

        var installer = new CanaryInstaller(store, new CredentialVault(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (args is ["remove"])
        {
            var cleaned = installer.Remove();
            Console.WriteLine(cleaned.Count == 0 ? "No canary tokens to remove." : $"Removed {cleaned.Count} canary location(s):");
            foreach (var location in cleaned)
                Console.WriteLine("  " + location);
            return 0;
        }

        if (args.Length > 0 && args[0] == "install")
        {
            var envFiles = new List<string>();
            for (var i = 1; i < args.Length; i++)
            {
                if (args[i] == "--env" && i + 1 < args.Length)
                {
                    envFiles.Add(args[++i]);
                    continue;
                }
                Console.Error.WriteLine($"Unknown canary option: {args[i]}");
                return 1;
            }
            try
            {
                var added = installer.Install(envFiles);
                Ui.Title($"{ProductInfo.Name} canary install");
                if (added.Count == 0)
                    Ui.Line(Ui.Dim("  All canary tokens are already in place."));
                foreach (var e in added)
                    Ui.Kv(e.Kind, $"{e.Location}  ({string.Join(", ", e.Tokens.Select(t => t.Name))})");
                Ui.Line(Ui.Dim("  No normal work uses these values. A use blocks the launcher and shows an alarm."));
                Ui.Line(Ui.Dim("  Undo: cw canary remove"));
                return 0;
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        Console.WriteLine("Usage: cw canary install [--env <file>]... | remove | status");
        Console.WriteLine($"  install  Fake tokens in the vault ({CanaryInstaller.DefaultVaultName}), in ~/.aws/credentials [{CanaryInstaller.AwsProfile}],");
        Console.WriteLine("           and in each .env template you name. A use of one is an attack.");
        Console.WriteLine("  remove   Delete every canary token.");
        return args.Length > 0 && args[0] is "-h" or "--help" or "help" ? 0 : 1;
    }
}
