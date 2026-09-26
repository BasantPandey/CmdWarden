using System.Text.Json.Nodes;
using CmdWarden.Cli.Hooks;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>#33 policy hook, without an Agent.</summary>
public class PolicyHookTests
{
    [Theory]
    [InlineData("gh pr create --title \"Release v1\"", "gh|pr create --title Release v1")]
    [InlineData("cd repo && git push origin main", "git|push origin main")]
    [InlineData("CW_REASON='ship it' GH_HOST=x gh pr merge 5", "gh|pr merge 5")]
    [InlineData("$env:CW_REASON = 'x'; & \"C:\\Program Files\\GitHub CLI\\gh.exe\" pr list", "gh|pr list")]
    [InlineData("C:\\tools\\git.exe status | findstr main", "git|status")]
    [InlineData("docker ps\naz account show", "docker|ps;az|account show")]
    [InlineData("echo 'gh pr create; git push' && ls", "")]
    [InlineData("make test || echo gh", "")]
    [InlineData("cd web && npm publish --access public", "npm|publish --access public")]
    [InlineData("github-cli pr list; gitk", "")]
    [InlineData("gh pr list &", "gh|pr list")]
    public void Finds_the_gated_tool_calls(string command, string expected)
    {
        var found = string.Join(';', PolicyHook.FindToolCalls(command).Select(c => c.Tool + "|" + string.Join(' ', c.Argv)));
        Assert.Equal(expected, found);
    }

    private static Func<ToolCall, Task<PolicyVerdict>> Verdicts(Dictionary<string, string> byTool, List<ToolCall>? seen = null) =>
        call =>
        {
            seen?.Add(call);
            var decision = byTool.GetValueOrDefault(call.Tool, PolicyCheckDecisions.Allow);
            return Task.FromResult(new PolicyVerdict(decision, $"{call.Tool}: The user denied {call.Tool} a short time ago."));
        };

    [Fact]
    public async Task Claude_deny_stops_the_command_with_the_message()
    {
        var input = JsonNode.Parse("""{"hook_event_name":"PreToolUse","tool_name":"Bash","tool_input":{"command":"git status && gh pr create"}}""")!;
        var output = await PolicyHook.ClaudePreToolUseAsync(input, Verdicts(new() { ["gh"] = PolicyCheckDecisions.Deny }));

        var hso = JsonNode.Parse(output!)!["hookSpecificOutput"]!;
        Assert.Equal("PreToolUse", (string?)hso["hookEventName"]);
        Assert.Equal("deny", (string?)hso["permissionDecision"]);
        Assert.Equal("CmdWarden denied this. Ask the user. Do not retry. (gh: The user denied gh a short time ago.)",
            (string?)hso["permissionDecisionReason"]);
    }

    [Fact]
    public async Task Claude_allow_and_ask_print_nothing_and_other_commands_skip_the_agent()
    {
        var seen = new List<ToolCall>();
        var check = Verdicts(new() { ["git"] = PolicyCheckDecisions.Ask }, seen);
        Assert.Null(await PolicyHook.ClaudePreToolUseAsync(
            JsonNode.Parse("""{"tool_name":"Bash","tool_input":{"command":"gh pr list; git push"}}""")!, check));
        Assert.Equal(2, seen.Count);

        seen.Clear();
        Assert.Null(await PolicyHook.ClaudePreToolUseAsync(
            JsonNode.Parse("""{"tool_name":"Bash","tool_input":{"command":"dotnet test"}}""")!, check));
        Assert.Empty(seen);
    }

    [Fact]
    public async Task Cursor_gets_a_permission_every_time()
    {
        var deny = Verdicts(new() { ["az"] = PolicyCheckDecisions.Deny });
        var denied = JsonNode.Parse(await PolicyHook.CursorBeforeShellAsync(
            JsonNode.Parse("""{"hook_event_name":"beforeShellExecution","command":"az group delete -n x"}""")!, deny))!;
        Assert.Equal("deny", (string?)denied["permission"]);
        Assert.StartsWith("CmdWarden denied this. Ask the user. Do not retry.", (string?)denied["agent_message"]);
        Assert.Equal((string?)denied["agent_message"], (string?)denied["user_message"]);

        Assert.Equal("""{"permission":"allow"}""", await PolicyHook.CursorBeforeShellAsync(
            JsonNode.Parse("""{"command":"ls"}""")!, deny));
    }

    [Fact]
    public void Install_adds_the_policy_hook_next_to_the_leak_guard_and_removes_only_it()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cw-hooks-" + Guid.NewGuid().ToString("N"));
        try
        {
            var claude = HookInstaller.ClaudeSettingsPath(dir);
            Assert.True(HookInstaller.InstallClaude(claude, "C:/tools/cw.exe leak-guard claude"));
            const string policy = "C:/tools/cw.exe hook check claude";
            Assert.True(HookInstaller.InstallClaude(claude, policy, "PreToolUse", PolicyHookCommands.ClaudeMatcher, HookInstaller.PolicyMarker));
            Assert.False(HookInstaller.InstallClaude(claude, policy, "PreToolUse", PolicyHookCommands.ClaudeMatcher, HookInstaller.PolicyMarker));

            var pre = JsonNode.Parse(File.ReadAllText(claude))!["hooks"]!["PreToolUse"]!.AsArray();
            Assert.Equal("Bash|PowerShell", (string?)Assert.Single(pre)!["matcher"]);
            Assert.Equal(policy, (string?)pre[0]!["hooks"]![0]!["command"]);

            Assert.True(HookInstaller.UninstallClaude(claude, "PreToolUse", HookInstaller.PolicyMarker));
            var hooks = JsonNode.Parse(File.ReadAllText(claude))!["hooks"]!;
            Assert.Empty(hooks["PreToolUse"]!.AsArray());
            Assert.Single(hooks["PostToolUse"]!.AsArray());

            var cursor = HookInstaller.CursorHooksPath(dir);
            Assert.True(HookInstaller.InstallCursor(cursor, "C:/tools/cw.exe leak-guard cursor"));
            Assert.True(HookInstaller.InstallCursor(cursor, "C:/tools/cw.exe hook check cursor",
                [PolicyHookCommands.CursorEvent], HookInstaller.PolicyMarker));
            Assert.True(HookInstaller.UninstallCursor(cursor, HookInstaller.PolicyMarker));
            var cursorHooks = JsonNode.Parse(File.ReadAllText(cursor))!["hooks"]!;
            Assert.Empty(cursorHooks[PolicyHookCommands.CursorEvent]!.AsArray());
            Assert.All(HookInstaller.CursorEvents, name => Assert.Single(cursorHooks[name]!.AsArray()));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}
