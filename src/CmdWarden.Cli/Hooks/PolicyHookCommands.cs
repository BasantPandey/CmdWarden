using System.Text.Json.Nodes;
using CmdWarden.Contracts;

namespace CmdWarden.Cli.Hooks;

/// <summary><c>cw hook</c> (#33): ask the Session Agent about policy before the harness runs a command.</summary>
public static class PolicyHookCommands
{
    public const string ClaudeMatcher = "Bash|PowerShell";
    public const string CursorEvent = "beforeShellExecution";

    public static async Task<int> HookAsync(string[] args)
    {
        switch (args)
        {
            case ["check", "claude" or "cursor"]:
                return await CheckAsync(args[1]).ConfigureAwait(false);
            case ["install", "claude"]:
                Report(HookInstaller.InstallClaude(HookInstaller.ClaudeSettingsPath(), HookInstaller.SelfCommand("hook check claude"),
                        "PreToolUse", ClaudeMatcher, HookInstaller.PolicyMarker),
                    "Claude Code PreToolUse hook", HookInstaller.ClaudeSettingsPath(), "added", "already there");
                return 0;
            case ["install", "cursor"]:
                Report(HookInstaller.InstallCursor(HookInstaller.CursorHooksPath(), HookInstaller.SelfCommand("hook check cursor"),
                        [CursorEvent], HookInstaller.PolicyMarker),
                    "Cursor beforeShellExecution hook", HookInstaller.CursorHooksPath(), "added", "already there");
                return 0;
            case ["uninstall", "claude"]:
                Report(HookInstaller.UninstallClaude(HookInstaller.ClaudeSettingsPath(), "PreToolUse", HookInstaller.PolicyMarker),
                    "Claude Code PreToolUse hook", HookInstaller.ClaudeSettingsPath(), "removed", "not there");
                return 0;
            case ["uninstall", "cursor"]:
                Report(HookInstaller.UninstallCursor(HookInstaller.CursorHooksPath(), HookInstaller.PolicyMarker),
                    "Cursor beforeShellExecution hook", HookInstaller.CursorHooksPath(), "removed", "not there");
                return 0;
            default:
                Console.WriteLine("Usage: cw hook install|uninstall claude|cursor");
                Console.WriteLine("  Check the policy before the harness runs a shell command.");
                Console.WriteLine("  A deny stops the command, and the agent reads: " + PolicyHook.DenyText(["<why>"]));
                Console.WriteLine("  Allow and ask add no step: the Approval Gate still asks when the command runs.");
                Console.WriteLine("The harness runs: cw hook check claude|cursor  (hook JSON on stdin).");
                return args.Length > 0 && args[0] is "-h" or "--help" or "help" ? 0 : 1;
        }
    }

    private static void Report(bool changed, string what, string path, string yes, string no) =>
        Console.WriteLine($"{what}: {(changed ? yes : no)} ({path})");

    /// <summary>
    /// The hook itself. It never blocks for its own failure: the shim still gates the run. A command
    /// with no gated tool makes no call to the Agent.
    /// </summary>
    private static async Task<int> CheckAsync(string harness)
    {
        JsonNode? input;
        try
        {
            input = JsonNode.Parse(await Console.In.ReadToEndAsync().ConfigureAwait(false));
        }
        catch (System.Text.Json.JsonException)
        {
            input = null;
        }
        if (input is null)
        {
            if (harness == "cursor")
                Console.Out.Write("""{"permission":"allow"}""");
            return 0;
        }

        var output = harness == "claude"
            ? await PolicyHook.ClaudePreToolUseAsync(input, AskAgentAsync).ConfigureAwait(false)
            : await PolicyHook.CursorBeforeShellAsync(input, AskAgentAsync).ConfigureAwait(false);
        if (output is not null)
            Console.Out.Write(output);
        return 0;
    }

    private static async Task<PolicyVerdict> AskAgentAsync(ToolCall call)
    {
        try
        {
            var r = await AgentPolicyClient.CheckAsync(call.Tool, call.Argv, timeout: TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            return new PolicyVerdict(r.Decision, $"{call.Tool}: {r.Message}");
        }
        catch (Exception)
        {
            return new PolicyVerdict(PolicyCheckDecisions.Allow, "");
        }
    }
}
