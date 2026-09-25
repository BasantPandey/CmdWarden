using System.Text.Json.Nodes;
using CmdWarden.Cli.Canary;
using CmdWarden.Cli.Hooks;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>#27 leak guard and #29 canary files, without an Agent.</summary>
public class LeakGuardTests
{
    private const string Token = "ghp_leakguardtestvalue0123456789";

    private static readonly KnownSecret[] Vault = [new("GH_TOKEN", Token), new("SHORT", "abc")];

    private static Task<LeakCheck> Check(IReadOnlyList<string> texts, string source)
    {
        var r = LeakRedactor.Redact(texts, Vault);
        return Task.FromResult(new LeakCheck(r.Texts, r.Matches.Select(m => m.Name).ToList()));
    }

    [Fact]
    public void Redact_replaces_every_copy_and_leaves_clean_text_alone()
    {
        var r = LeakRedactor.Redact([$"a {Token} b {Token}", "no secrets here abc"], Vault);
        Assert.Equal("a [CmdWarden: GH_TOKEN] b [CmdWarden: GH_TOKEN]", r.Texts[0]);
        Assert.Equal("no secrets here abc", r.Texts[1]);
        Assert.Equal(["GH_TOKEN"], r.Matches.Select(m => m.Name));
    }

    [Fact]
    public void Redact_replaces_the_longer_value_first()
    {
        var inner = new KnownSecret("INNER", "abcdefgh1");
        var outer = new KnownSecret("OUTER", "xxabcdefgh1yy");
        var r = LeakRedactor.Redact(["xxabcdefgh1yy"], [inner, outer]);
        Assert.Equal("[CmdWarden: OUTER]", r.Texts[0]);
    }

    [Fact]
    public async Task Claude_Bash_output_keeps_its_shape_with_the_placeholder()
    {
        var input = new JsonObject
        {
            ["hook_event_name"] = "PostToolUse",
            ["tool_name"] = "Bash",
            ["tool_input"] = new JsonObject { ["command"] = "echo $GH_TOKEN" },
            ["tool_response"] = new JsonObject { ["stdout"] = Token + "\n", ["stderr"] = "", ["interrupted"] = false, ["isImage"] = false },
        };
        var output = JsonNode.Parse((await LeakGuardHook.ClaudePostToolUseAsync(input, Check))!)!;
        var updated = output["hookSpecificOutput"]!["updatedToolOutput"]!;
        Assert.Equal("[CmdWarden: GH_TOKEN]\n", (string?)updated["stdout"]);
        Assert.False((bool)updated["interrupted"]!);
        Assert.Equal("PostToolUse", (string?)output["hookSpecificOutput"]!["hookEventName"]);
        Assert.DoesNotContain(Token, output.ToJsonString());
    }

    [Fact]
    public async Task Claude_Read_output_hides_the_value_in_the_file_content()
    {
        var input = new JsonObject
        {
            ["tool_name"] = "Read",
            ["tool_response"] = new JsonObject
            {
                ["type"] = "text",
                ["file"] = new JsonObject { ["filePath"] = @"C:\x\.env", ["content"] = "TOKEN=" + Token, ["numLines"] = 1 },
            },
        };
        var output = await LeakGuardHook.ClaudePostToolUseAsync(input, Check);
        Assert.Contains("TOKEN=[CmdWarden: GH_TOKEN]", output);
        Assert.DoesNotContain(Token, output);
    }

    [Fact]
    public async Task Claude_string_output_and_clean_output()
    {
        var mcp = JsonNode.Parse($$"""{"tool_name":"mcp__x__y","tool_response":"value {{Token}}"}""")!;
        var output = JsonNode.Parse((await LeakGuardHook.ClaudePostToolUseAsync(mcp, Check))!)!;
        Assert.Equal("value [CmdWarden: GH_TOKEN]", (string?)output["hookSpecificOutput"]!["updatedToolOutput"]);

        var clean = JsonNode.Parse("""{"tool_name":"Bash","tool_response":{"stdout":"hello","stderr":""}}""")!;
        Assert.Null(await LeakGuardHook.ClaudePostToolUseAsync(clean, Check));
    }

    [Fact]
    public async Task Cursor_blocks_a_read_that_holds_a_value()
    {
        var hit = JsonNode.Parse($$"""{"hook_event_name":"beforeReadFile","file_path":"C:\\a\\.env","content":"T={{Token}}"}""")!;
        var denied = JsonNode.Parse((await LeakGuardHook.CursorAsync(hit, Check))!)!;
        Assert.Equal("deny", (string?)denied["permission"]);
        Assert.Contains("GH_TOKEN", (string?)denied["user_message"]);

        var clean = JsonNode.Parse("""{"hook_event_name":"beforeReadFile","file_path":"a","content":"hello"}""")!;
        Assert.Equal("allow", (string?)JsonNode.Parse((await LeakGuardHook.CursorAsync(clean, Check))!)!["permission"]);
    }

    [Fact]
    public async Task Cursor_replaces_MCP_output()
    {
        var input = new JsonObject
        {
            ["hook_event_name"] = "postToolUse",
            ["tool_name"] = "MCP:github",
            ["tool_output"] = $$"""{"text":"{{Token}}"}""",
        };
        var output = JsonNode.Parse((await LeakGuardHook.CursorAsync(input, Check))!)!;
        Assert.Equal("[CmdWarden: GH_TOKEN]", (string?)output["updated_mcp_tool_output"]!["text"]);
    }

    [Fact]
    public void Hook_install_keeps_other_settings_and_runs_once()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cw-hooks-" + Guid.NewGuid().ToString("N"));
        try
        {
            var claude = HookInstaller.ClaudeSettingsPath(dir);
            Directory.CreateDirectory(Path.GetDirectoryName(claude)!);
            File.WriteAllText(claude, """{"model":"opus","hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"x"}]}]}}""");
            var command = "C:/tools/cw.exe leak-guard claude";

            Assert.True(HookInstaller.InstallClaude(claude, command));
            Assert.False(HookInstaller.InstallClaude(claude, command));
            var root = JsonNode.Parse(File.ReadAllText(claude))!;
            Assert.Equal("opus", (string?)root["model"]);
            Assert.Single(root["hooks"]!["PreToolUse"]!.AsArray());
            Assert.Equal(command, (string?)root["hooks"]!["PostToolUse"]![0]!["hooks"]![0]!["command"]);

            Assert.True(HookInstaller.UninstallClaude(claude));
            root = JsonNode.Parse(File.ReadAllText(claude))!;
            Assert.Empty(root["hooks"]!["PostToolUse"]!.AsArray());
            Assert.Single(root["hooks"]!["PreToolUse"]!.AsArray());

            var cursor = HookInstaller.CursorHooksPath(dir);
            Assert.True(HookInstaller.InstallCursor(cursor, "C:/tools/cw.exe leak-guard cursor"));
            Assert.False(HookInstaller.InstallCursor(cursor, "C:/tools/cw.exe leak-guard cursor"));
            var hooks = JsonNode.Parse(File.ReadAllText(cursor))!;
            Assert.Equal(1, (int)hooks["version"]!);
            foreach (var name in HookInstaller.CursorEvents)
                Assert.Single(hooks["hooks"]![name]!.AsArray());
            Assert.True(HookInstaller.UninstallCursor(cursor));
            Assert.All(HookInstaller.CursorEvents, name =>
                Assert.Empty(JsonNode.Parse(File.ReadAllText(cursor))!["hooks"]![name]!.AsArray()));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Canary_install_then_remove_restores_every_file_and_the_vault()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var home = Path.Combine(Path.GetTempPath(), "cw-canary-" + Guid.NewGuid().ToString("N"));
        var vaultName = "CW_TEST_CANARY_" + Guid.NewGuid().ToString("N")[..8];
        var vault = new CredentialVault();
        try
        {
            var aws = Path.Combine(home, ".aws", "credentials");
            Directory.CreateDirectory(Path.GetDirectoryName(aws)!);
            const string original = "[default]\r\naws_access_key_id = AKIAREALREALREAL1234\r\n";
            File.WriteAllText(aws, original);
            var env = Path.Combine(home, "app", ".env.example");
            var store = new CanaryStore(home);
            var installer = new CanaryInstaller(store, vault, home, vaultName);

            var added = installer.Install([env]);

            Assert.Equal(3, added.Count);
            Assert.Contains("[backup-admin]", File.ReadAllText(aws));
            Assert.StartsWith(original, File.ReadAllText(aws));
            Assert.StartsWith("GITHUB_TOKEN=ghp_", File.ReadAllText(env));
            Assert.NotNull(vault.ReadTarget(VaultNames.TargetName(vaultName)));
            Assert.Equal(4, store.Secrets().Count);
            Assert.Empty(installer.Install([env]));

            var cleaned = installer.Remove();

            Assert.Equal(3, cleaned.Count);
            Assert.Equal(original, File.ReadAllText(aws));
            Assert.False(File.Exists(env));
            Assert.Null(vault.ReadTarget(VaultNames.TargetName(vaultName)));
            Assert.Empty(store.Load());
        }
        finally
        {
            vault.DeleteTarget(VaultNames.TargetName(vaultName));
            try { Directory.Delete(home, recursive: true); } catch { /* ignore */ }
        }
    }
}
