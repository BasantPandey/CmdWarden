using System.Text;
using System.Text.Json.Nodes;
using CmdWarden.Cli;
using CmdWarden.Cli.Hooks;
using CmdWarden.Cli.Mcp;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>v0.8.0 logic without an Agent: #66 Codex, #67 .env guard, #68 prompt count, #69 mask, #70 protections.</summary>
public class V080UnitTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cw-v080-" + Guid.NewGuid().ToString("N"));

    public V080UnitTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    private static readonly KeyValuePair<string, string>[] Secret = [new("API_KEY", "sk-live-1234567890")];

    private static string Mask(OutputMask mask, params string[] chunks)
    {
        var output = new List<byte>();
        foreach (var chunk in chunks)
            output.AddRange(mask.Push(Encoding.UTF8.GetBytes(chunk)));
        output.AddRange(mask.Flush());
        return Encoding.UTF8.GetString(output.ToArray());
    }

    [Fact]
    public void Mask_replaces_a_value_also_when_a_read_splits_it()
    {
        Assert.Equal("key=[CmdWarden: API_KEY] ok", Mask(new OutputMask(Secret), "key=sk-live-1234567890 ok"));
        Assert.Equal("key=[CmdWarden: API_KEY] ok", Mask(new OutputMask(Secret), "key=sk-li", "ve-12345", "67890 ok"));
    }

    [Fact]
    public void Mask_holds_back_only_a_possible_start_of_a_value()
    {
        var mask = new OutputMask(Secret);
        Assert.Equal("ready on port 3000\n", Encoding.UTF8.GetString(mask.Push(Encoding.UTF8.GetBytes("ready on port 3000\n"))));
        Assert.Equal("value ", Encoding.UTF8.GetString(mask.Push(Encoding.UTF8.GetBytes("value sk-"))));
        Assert.Equal("sk-", Encoding.UTF8.GetString(mask.Flush()));
    }

    [Fact]
    public void Mask_keeps_other_bytes_as_they_are_and_skips_short_values()
    {
        var mask = new OutputMask([new("SHORT", "abc"), .. Secret]);
        var bytes = new byte[] { 0x82, 0xFF, (byte)'a', (byte)'b', (byte)'c', 0x0D, 0x0A };
        var output = mask.Push(bytes).Concat(mask.Flush()).ToArray();
        Assert.Equal(bytes, output);
    }

    [Fact]
    public void DotEnv_reads_keys_quotes_exports_comments_and_refs()
    {
        var lines = DotEnvFile.Parse("# comment\n\nexport API_KEY=\"sk live\"\nPORT=3000 # web\nDB_PASSWORD='p#w'\nGH_TOKEN=cw://GH_TOKEN\nnot a line");
        Assert.Null(lines[0].Key);
        Assert.Null(lines[1].Key);
        Assert.Equal(("API_KEY", "sk live", true), (lines[2].Key, lines[2].Value, lines[2].Export));
        Assert.Equal(("PORT", "3000"), (lines[3].Key, lines[3].Value));
        Assert.Equal("p#w", lines[4].Value);
        Assert.Equal("GH_TOKEN", lines[5].VaultRef);
        Assert.Null(lines[6].Key);
        Assert.Equal("export API_KEY=cw://myapp_API_KEY", DotEnvFile.RefLine(lines[2], "myapp_API_KEY"));
        Assert.True(DotEnvFile.LooksSecret("STRIPE_SECRET_KEY"));
        Assert.True(DotEnvFile.LooksSecret("DATABASE_URL"));
        Assert.False(DotEnvFile.LooksSecret("PORT"));
    }

    [Fact]
    public void Env_import_moves_secret_values_and_keeps_the_rest()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var prefix = "t" + Guid.NewGuid().ToString("N")[..8] + "_";
        var file = Path.Combine(_dir, ".env");
        File.WriteAllText(file, "# app\r\nPORT=3000\r\nAPI_KEY=sk-live-1234567890\r\nGH_TOKEN=cw://GH_TOKEN\r\n");
        var vault = new CredentialVault();
        try
        {
            var exit = EnvCommands.Run(["import", file, "--prefix", prefix], vault, new StringReader("y\n"));

            Assert.Equal(0, exit);
            Assert.Equal($"# app\r\nPORT=3000\r\nAPI_KEY=cw://{prefix}API_KEY\r\nGH_TOKEN=cw://GH_TOKEN\r\n", File.ReadAllText(file));
            Assert.Equal("sk-live-1234567890", Encoding.UTF8.GetString(vault.Read(prefix + "API_KEY")));
            Assert.Equal(0, EnvCommands.Run(["import", file, "--prefix", prefix], vault, new StringReader("")));
        }
        finally
        {
            vault.Delete(prefix + "API_KEY");
        }
    }

    [Fact]
    public void Env_import_refuses_a_different_value_in_the_vault()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var prefix = "t" + Guid.NewGuid().ToString("N")[..8] + "_";
        var file = Path.Combine(_dir, ".env");
        File.WriteAllText(file, "API_KEY=new-value-1234\n");
        var vault = new CredentialVault();
        vault.Save(prefix + "API_KEY", Encoding.UTF8.GetBytes("old-value-1234"));
        try
        {
            Assert.Equal(1, EnvCommands.Run(["import", file, "--prefix", prefix, "--yes"], vault));
            Assert.Equal("API_KEY=new-value-1234\n", File.ReadAllText(file));
            Assert.Equal("old-value-1234", Encoding.UTF8.GetString(vault.Read(prefix + "API_KEY")));
        }
        finally
        {
            vault.Delete(prefix + "API_KEY");
        }
    }

    [Theory]
    [InlineData("cat .env", ".env")]
    [InlineData("Get-Content ./app/.env.local | Select-String KEY", "./app/.env.local")]
    [InlineData("source .env && npm start", ".env")]
    [InlineData("type .env.example", "")]
    [InlineData("ls .env", "")]
    public void Finds_dotenv_reads(string command, string expected) =>
        Assert.Equal(expected, string.Join(';', PolicyHook.EnvFileReads(command)));

    [Fact]
    public async Task Hook_denies_a_dotenv_read_only_while_it_holds_a_plain_secret()
    {
        File.WriteAllText(Path.Combine(_dir, ".env"), "PORT=3000\nAPI_KEY=sk-live-1234567890\n");
        static Task<PolicyVerdict> Allow(ToolCall _) => Task.FromResult(new PolicyVerdict(PolicyCheckDecisions.Allow, ""));
        JsonNode Bash(string command) => new JsonObject { ["tool_name"] = "Bash", ["cwd"] = _dir, ["tool_input"] = new JsonObject { ["command"] = command } };

        var denied = JsonNode.Parse((await PolicyHook.ClaudePreToolUseAsync(Bash("cat .env"), Allow))!)!["hookSpecificOutput"]!;
        Assert.Equal("deny", (string?)denied["permissionDecision"]);
        Assert.Contains("cw inject --env-file .env", (string?)denied["permissionDecisionReason"]);

        var read = new JsonObject { ["tool_name"] = "Read", ["cwd"] = _dir, ["tool_input"] = new JsonObject { ["file_path"] = Path.Combine(_dir, ".env") } };
        Assert.NotNull(await PolicyHook.ClaudePreToolUseAsync(read, Allow));

        File.WriteAllText(Path.Combine(_dir, ".env"), "PORT=3000\nAPI_KEY=cw://API_KEY\n");
        Assert.Null(await PolicyHook.ClaudePreToolUseAsync(Bash("cat .env"), Allow));
        Assert.Null(await PolicyHook.ClaudePreToolUseAsync(read, Allow));
    }

    [Theory]
    [InlineData("cw policy set auth:sha1:abc gh Full", "cw policy set")]
    [InlineData("cmdwarden.exe unharden gh && echo done", "cw unharden")]
    [InlineData("C:/tools/cw.exe hook uninstall claude", "cw hook uninstall")]
    [InlineData("cw policy list; cw audit -n 5; cw inject +T -- x", "")]
    [InlineData("echo cw policy set", "")]
    public void Finds_cw_commands_that_weaken_protection(string command, string expected) =>
        Assert.Equal(expected, string.Join(';', PolicyHook.WeakeningCwCalls(command)));

    [Fact]
    public async Task Hook_denies_a_harness_that_turns_protection_off()
    {
        static Task<PolicyVerdict> Allow(ToolCall _) => Task.FromResult(new PolicyVerdict(PolicyCheckDecisions.Allow, ""));
        var input = new JsonObject { ["tool_name"] = "Bash", ["tool_input"] = new JsonObject { ["command"] = "cw policy set k gh Full" } };
        var reason = (string?)JsonNode.Parse((await PolicyHook.ClaudePreToolUseAsync(input, Allow))!)!["hookSpecificOutput"]!["permissionDecisionReason"];
        Assert.Contains(PolicyHook.SelfProtectText, reason);

        var cursor = JsonNode.Parse(await PolicyHook.CursorBeforeShellAsync(JsonNode.Parse("""{"command":"cw unharden git"}""")!, Allow))!;
        Assert.Equal("deny", (string?)cursor["permission"]);
    }

    [Fact]
    public async Task Codex_leak_guard_replaces_the_result_with_the_placeholders()
    {
        static Task<LeakCheck> Check(IReadOnlyList<string> texts, string source) =>
            Task.FromResult(new LeakCheck(texts.Select(t => t.Replace("sk-live-1234567890", "[CmdWarden: API_KEY]")).ToList(),
                texts.Any(t => t.Contains("sk-live")) ? ["API_KEY"] : []));

        var input = JsonNode.Parse("""{"hook_event_name":"PostToolUse","tool_name":"Bash","tool_response":"key sk-live-1234567890"}""")!;
        var output = JsonNode.Parse((await LeakGuardHook.CodexPostToolUseAsync(input, Check))!)!;
        Assert.Equal("block", (string?)output["decision"]);
        Assert.Equal("key [CmdWarden: API_KEY]", (string?)output["reason"]);
        Assert.Contains("API_KEY", (string?)output["hookSpecificOutput"]!["additionalContext"]);

        Assert.Null(await LeakGuardHook.CodexPostToolUseAsync(JsonNode.Parse("""{"tool_response":"clean"}""")!, Check));
    }

    [Fact]
    public void Codex_hooks_use_the_claude_shape_and_update_an_old_matcher()
    {
        var path = HookInstaller.CodexHooksPath(_dir);
        Assert.True(HookInstaller.InstallClaude(path, "cw hook check codex", "PreToolUse", "Bash", HookInstaller.PolicyMarker));
        Assert.False(HookInstaller.InstallClaude(path, "cw hook check codex", "PreToolUse", "Bash", HookInstaller.PolicyMarker));
        Assert.True(HookInstaller.InstallClaude(path, "cw hook check codex", "PreToolUse", "Bash|Read", HookInstaller.PolicyMarker));
        var entry = JsonNode.Parse(File.ReadAllText(path))!["hooks"]!["PreToolUse"]![0]!;
        Assert.Equal("Bash|Read", (string?)entry["matcher"]);
        Assert.Equal("cw hook check codex", (string?)entry["hooks"]![0]!["command"]);
        Assert.True(HookInstaller.UninstallClaude(path, "PreToolUse", HookInstaller.PolicyMarker));
    }

    [Fact]
    public void Codex_config_block_is_added_updated_removed_and_never_takes_a_user_table()
    {
        var path = CodexConfig.Path(_dir);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "model = \"gpt-5\"\n\n[mcp_servers.other]\ncommand = \"x\"\n");

        Assert.Equal(CodexConfig.Change.Added, McpCommands.InstallCodex(path, [@"C:\Program Files\cw\cw.exe", "mcp"]));
        Assert.Equal(CodexConfig.Change.Unchanged, McpCommands.InstallCodex(path, [@"C:\Program Files\cw\cw.exe", "mcp"]));
        var text = File.ReadAllText(path);
        Assert.Contains("[mcp_servers.cmdwarden]\ncommand = 'C:\\Program Files\\cw\\cw.exe'\nargs = ['mcp']", text);
        Assert.StartsWith("model = \"gpt-5\"\n\n[mcp_servers.other]", text);
        Assert.Equal(CodexConfig.Change.Updated, McpCommands.InstallCodex(path, [@"C:\cw\cw.exe", "mcp"]));

        Assert.True(CodexConfig.Remove(path, McpCommands.CodexBlock));
        Assert.Equal("model = \"gpt-5\"\n\n[mcp_servers.other]\ncommand = \"x\"\n", File.ReadAllText(path));

        File.AppendAllText(path, "\n[shell_environment_policy]\ninherit = \"core\"\n");
        Assert.Equal(CodexConfig.Change.UserOwned, HarnessProtections.InstallCodex(["API_KEY"], path));
    }

    [Fact]
    public void Claude_protections_add_and_remove_only_what_cw_added()
    {
        var settings = Path.Combine(_dir, "settings.json");
        File.WriteAllText(settings, """{"permissions":{"deny":["Read(~/.npmrc)","Bash(rm:*)"]},"env":{"FOO":"1"}}""");

        var added = HarnessProtections.InstallClaude(settings, _dir);
        Assert.DoesNotContain("Read(~/.npmrc)", added);
        Assert.Contains("Read(**/.env)", added);
        Assert.Contains($"env {HarnessProtections.ScrubVariable}=1", added);
        Assert.Empty(HarnessProtections.InstallClaude(settings, _dir));

        HarnessProtections.UninstallClaude(settings, _dir);
        var root = JsonNode.Parse(File.ReadAllText(settings))!;
        Assert.Equal(["Read(~/.npmrc)", "Bash(rm:*)"], root["permissions"]!["deny"]!.AsArray().Select(n => (string)n!));
        Assert.Equal("1", (string?)root["env"]!["FOO"]);
        Assert.Null(root["env"]![HarnessProtections.ScrubVariable]);
    }

    [Fact]
    public void Prompt_count_counts_the_card_answers_of_today()
    {
        var now = DateTimeOffset.Now;
        AuditGateRecord Row(string decision, string? reason, DateTimeOffset at) =>
            new() { Ts = at.UtcDateTime.ToString("o"), Decision = decision, ReasonCode = reason, Tool = "gh" };
        var rows = new[]
        {
            Row(GateDecisions.AllowOnce, null, now),
            Row(GateDecisions.SessionGrant, null, now),
            Row(GateDecisions.Deny, PolicyReasonCodes.UserDenied, now),
            Row(GateDecisions.AllowOnce, PolicyReasonCodes.TransientReuse, now),
            Row(GateDecisions.AutoAllow, null, now),
            Row(GateDecisions.Deny, PolicyReasonCodes.CanaryHit, now),
            Row(GateDecisions.AllowOnce, null, now.AddDays(-1)),
        };
        Assert.Equal(3, PromptCount.Today(rows, now));
    }

    [Fact]
    public void A_new_policy_file_allows_low_risk_writes_and_an_old_one_keeps_ask()
    {
        var fresh = new PolicyStore(Path.Combine(_dir, "new.json"));
        fresh.Load();
        Assert.True(fresh.LowRiskWritesAllowed);

        var old = Path.Combine(_dir, "old.json");
        File.WriteAllText(old, """{"defaults":{"aiHarness":"Read","terminal":"Trusted"},"launchers":{}}""");
        var store = new PolicyStore(old);
        store.Load();
        Assert.False(store.LowRiskWritesAllowed);
    }
}
