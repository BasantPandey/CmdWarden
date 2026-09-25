using System.Diagnostics;
using System.Text.Json.Nodes;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Scan;

namespace CmdWarden.Tests;

/// <summary>#28: plain secrets in MCP and harness config files, and Move to vault.</summary>
public class McpConfigSecretsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cw-mcp-" + Guid.NewGuid().ToString("N"));
    private readonly string _key = "CW_TEST_MCP_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant() + "_TOKEN";
    private const string Value = "ghp_mcpTestValue0123456789abcdefABCDEF";

    public McpConfigSecretsTests() => Directory.CreateDirectory(Path.Combine(_dir, "home", ".claude"));

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
            new CredentialVault().Delete(_key);
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    private string WriteProjectConfig()
    {
        var path = Path.Combine(_dir, ".mcp.json");
        File.WriteAllText(path, $$"""
            {
              // comments are allowed in these files
              "mcpServers": {
                "github": {
                  "command": "cmd",
                  "args": ["/c", "server.cmd"],
                  "env": { "{{_key}}": "{{Value}}", "SAFE": "${GH_TOKEN}", "LOG_LEVEL": "debug" }
                },
                "remote": {
                  "type": "http",
                  "url": "https://mcp.example.test",
                  "headers": { "Authorization": "Bearer {{Value}}" }
                }
              }
            }
            """);
        File.WriteAllText(Path.Combine(_dir, "home", ".claude", "settings.json"),
            """{"env": {"ANTHROPIC_API_KEY": "sk-ant-api03-testvalue000000000000", "CLAUDE_CODE_USE_BEDROCK": "0"}}""");
        return path;
    }

    private ScanContext Context() => new(productRoot: _dir, pathEnv: "", getEnv: _ => null,
        userProfile: Path.Combine(_dir, "home"), workingDirectory: _dir, appData: Path.Combine(_dir, "appdata"));

    [Fact]
    public void Scan_reports_each_plain_value_and_never_the_value()
    {
        var file = WriteProjectConfig();
        var findings = new McpConfigSecretDetector().Detect(Context());

        Assert.Equal(3, findings.Count);
        Assert.All(findings, f => Assert.DoesNotContain(Value, f.Evidence + f.Summary + f.Fix));
        var env = Assert.Single(findings, f => f.Evidence.Contains(_key));
        Assert.StartsWith(file, env.Evidence);
        Assert.NotNull(McpSecretLocation.FromFix(env.Fix));
        Assert.Contains(findings, f => f.Evidence.Contains("headers.Authorization") && f.Fix is null);
        Assert.Contains(findings, f => f.Evidence.Contains("ANTHROPIC_API_KEY") && f.Fix is null);
        Assert.Contains(new ScanEngine().Detectors, d => d is McpConfigSecretDetector);
    }

    [Theory]
    [InlineData("GITHUB_TOKEN", "ghp_abcdefghijklmnop", true)]
    [InlineData("ANY", "github_pat_11ABCDEFG", true)]
    [InlineData("API_KEY", "abcdefgh12345678", true)]
    [InlineData("GITHUB_TOKEN", "${GITHUB_TOKEN}", false)]
    [InlineData("GITHUB_TOKEN", "<your-token>", false)]
    [InlineData("GITHUB_TOKEN", "your_token_here", false)]
    [InlineData("GITHUB_TOKEN", "[CmdWarden: GITHUB_TOKEN]", false)]
    [InlineData("LOG_LEVEL", "debugging-verbose", false)]
    [InlineData("GITHUB_TOKEN", "short", false)]
    public void LooksSecret_rules(string name, string value, bool expected) =>
        Assert.Equal(expected, McpConfigFiles.LooksSecret(name, value));

    [Fact]
    public void Move_to_vault_leaves_a_placeholder_and_wraps_the_command()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var file = WriteProjectConfig();
        var fix = McpSecretLocation.FromFix(new McpConfigSecretDetector().Detect(Context()).Single(f => f.Fix is not null).Fix)!;
        var vault = new CredentialVault();
        string[] cw = [@"C:\tools\cw.exe"];

        Assert.Equal(_key, McpSecretMover.Move(fix, vault, cw));

        var text = File.ReadAllText(file);
        Assert.DoesNotContain(Value, text.Replace("Bearer " + Value, ""));
        var server = JsonNode.Parse(text)!["mcpServers"]!["github"]!;
        Assert.Equal($"[CmdWarden: {_key}]", (string?)server["env"]![_key]);
        Assert.Equal(@"C:\tools\cw.exe", (string?)server["command"]);
        Assert.Equal(["inject", "--tool", "mcp", "--class", "read", "+" + _key, "--", "cmd", "/c", "server.cmd"],
            server["args"]!.AsArray().Select(a => (string?)a));
        Assert.Equal(Value, CredentialVault.Utf8(vault.ReadTarget(VaultNames.TargetName(_key))!.Blob));
        Assert.DoesNotContain(new McpConfigSecretDetector().Detect(Context()), f => f.Fix is not null);
    }

    [Fact]
    public void Move_stops_before_any_change_when_the_vault_holds_another_value()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var file = WriteProjectConfig();
        var before = File.ReadAllText(file);
        var vault = new CredentialVault();
        vault.SaveTarget(VaultNames.TargetName(_key), null, CredentialVault.Utf8Bytes("different-value-123"));
        var fix = McpSecretLocation.FromFix(new McpConfigSecretDetector().Detect(Context()).Single(f => f.Fix is not null).Fix)!;

        Assert.Throws<InvalidOperationException>(() => McpSecretMover.Move(fix, vault, ["cw"]));
        Assert.Equal(before, File.ReadAllText(file));
    }
}

/// <summary>#28 done-when: after Move to vault the MCP server still starts, and gets the value.</summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class McpMoveProcessTests
{
    [Fact]
    public async Task Moved_server_starts_through_cw_inject_with_the_value()
    {
        if (!OperatingSystem.IsWindows())
            return;
        await using var fx = await ApprovalMemoryFixture.CreateAsync("deny");
        if (fx is null)
            return;
        var dir = Path.Combine(fx.ProductRoot, "project");
        Directory.CreateDirectory(dir);
        var key = "CW_TEST_MCP_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant() + "_TOKEN";
        var value = "ghp_mcpProcessValue" + Guid.NewGuid().ToString("N");
        var server = Path.Combine(dir, "server.cmd");
        File.WriteAllText(server, $"@echo off\r\nif \"%{key}%\"==\"{value}\" (echo server-started & exit /b 0)\r\necho wrong-value\r\nexit /b 5\r\n");
        var config = Path.Combine(dir, ".mcp.json");
        File.WriteAllText(config, new JsonObject
        {
            ["mcpServers"] = new JsonObject
            {
                ["github"] = new JsonObject
                {
                    ["command"] = "cmd",
                    ["args"] = new JsonArray("/c", server),
                    ["env"] = new JsonObject { [key] = value },
                },
            },
        }.ToJsonString());
        var vault = new CredentialVault();
        try
        {
            var context = new ScanContext(productRoot: fx.ProductRoot, pathEnv: "", getEnv: _ => null,
                userProfile: dir, workingDirectory: dir, appData: dir);
            var fix = McpSecretLocation.FromFix(new McpConfigSecretDetector().Detect(context).Single().Fix)!;
            McpSecretMover.Move(fix, vault, ["dotnet", TestPaths.FindCliDll()]);

            // Start the server the way a harness does: command, args, and env from the file.
            var entry = JsonNode.Parse(File.ReadAllText(config))!["mcpServers"]!["github"]!;
            Assert.DoesNotContain(value, File.ReadAllText(config));
            var psi = new ProcessStartInfo((string)entry["command"]!)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in entry["args"]!.AsArray())
                psi.ArgumentList.Add((string)a!);
            foreach (var (k, v) in entry["env"]!.AsObject())
                psi.Environment[k] = (string?)v;
            psi.Environment["CW_PIPE_NAME"] = fx.PipeName;
            psi.Environment["CW_POLICY_PATH"] = fx.PolicyPath;
            psi.Environment[ProductPaths.EnvVar] = fx.ProductRoot;
            using var process = Process.Start(psi)!;
            var output = await process.StandardOutput.ReadToEndAsync() + await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.True(process.ExitCode == 0, output);
            Assert.Contains("server-started", output);
            Assert.Contains(fx.AuditLines(), l => l.Contains("\"tool\":\"mcp\"", StringComparison.Ordinal) && l.Contains(key, StringComparison.Ordinal));
        }
        finally
        {
            vault.Delete(key);
        }
    }
}
