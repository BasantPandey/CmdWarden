using System.Text.Json;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>docker-credential-cmdwarden protocol (#203): argv, stdin payload, exact stdout lines, exit codes.</summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class DockerHelperProtocolTests
{
    private static async Task<(int Exit, string Out)> RunAsync(string? pipeName, string stdin, params string[] args)
    {
        var stdout = new StringWriter();
        var exit = await DockerHelperApp.RunAsync(args, new StringReader(stdin), stdout, pipeName);
        return (exit, stdout.ToString());
    }

    private static string NewUrl() => "https://registry-" + Guid.NewGuid().ToString("N")[..8] + ".example.test/v1/";

    [Fact]
    public async Task Version_and_help_exit_0()
    {
        var (exit, output) = await RunAsync(null, "", "version");
        Assert.Equal(0, exit);
        Assert.StartsWith(DockerHelperApp.Name + " (CmdWarden) ", output);

        (exit, output) = await RunAsync(null, "", "--help");
        Assert.Equal(0, exit);
        Assert.Contains("Usage:", output);
    }

    [Fact]
    public async Task Wrong_argument_count_prints_usage_and_exits_1()
    {
        foreach (var args in new[] { Array.Empty<string>(), new[] { "get", "extra" } })
        {
            var (exit, output) = await RunAsync(null, "", args);
            Assert.Equal(1, exit);
            Assert.Contains("Usage:", output);
        }
    }

    [Fact]
    public async Task Unknown_action_exits_1()
    {
        var (exit, output) = await RunAsync(null, "", "frobnicate");
        Assert.Equal(1, exit);
        Assert.Equal(DockerHelperApp.Name + ": unknown action: frobnicate" + Environment.NewLine, output);
    }

    [Fact]
    public async Task Get_and_erase_with_empty_stdin_print_the_missing_url_line()
    {
        foreach (var action in new[] { "get", "erase" })
        {
            var (exit, output) = await RunAsync(null, "  \n", action);
            Assert.Equal(1, exit);
            Assert.Equal(DockerHelperApp.MissingUrlLine + Environment.NewLine, output);
        }
    }

    [Fact]
    public void ParseStore_applies_docker_isValid()
    {
        var ok = DockerHelperApp.ParseStore("""{"ServerURL":"https://r.example.test/","Username":"u","Secret":"s"}""", out var error);
        Assert.NotNull(ok);
        Assert.Equal("", error);
        Assert.Equal("s", ok!.Secret);

        Assert.Null(DockerHelperApp.ParseStore("""{"Username":"u","Secret":"s"}""", out error));
        Assert.Equal(DockerHelperApp.MissingUrlLine, error);

        Assert.Null(DockerHelperApp.ParseStore("""{"ServerURL":"https://r.example.test/","Secret":"s"}""", out error));
        Assert.Equal(DockerHelperApp.MissingUsernameLine, error);

        Assert.Null(DockerHelperApp.ParseStore("not json", out error));
        Assert.StartsWith(DockerHelperApp.Name + ": invalid store payload:", error);
    }

    [Fact]
    public async Task Store_with_invalid_payload_exits_1_before_any_rpc()
    {
        var (exit, output) = await RunAsync(null, """{"ServerURL":"https://r.example.test/"}""", "store");
        Assert.Equal(1, exit);
        Assert.Equal(DockerHelperApp.MissingUsernameLine + Environment.NewLine, output);
    }

    [Fact]
    public async Task Store_get_list_erase_round_trip_in_docker_shapes()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "off", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        Assert.NotNull(TestChain.PinParentAsDocker(fx.ProductRoot));
        var url = NewUrl();
        var secret = "pw-" + Guid.NewGuid().ToString("N");
        try
        {
            var stored = await RunAsync(fx.PipeName, $$"""{"ServerURL":"{{url}}","Username":"alice","Secret":"{{secret}}"}""", "store");
            Assert.Equal((0, ""), stored);

            var got = await RunAsync(fx.PipeName, url + "\n", "get");
            Assert.Equal(0, got.Exit);
            var creds = JsonSerializer.Deserialize<DockerHelperApp.Credentials>(got.Out)!;
            Assert.Equal(url, creds.ServerURL);
            Assert.Equal("alice", creds.Username);
            Assert.Equal(secret, creds.Secret);

            var listed = await RunAsync(fx.PipeName, "unused", "list");
            Assert.Equal(0, listed.Exit);
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(listed.Out)!;
            Assert.Equal("alice", map[url]);
            Assert.DoesNotContain(secret, listed.Out, StringComparison.Ordinal);

            var erased = await RunAsync(fx.PipeName, url, "erase");
            Assert.Equal((0, ""), erased);

            var missing = await RunAsync(fx.PipeName, url, "get");
            Assert.Equal(1, missing.Exit);
            Assert.Equal(DockerHelperApp.NotFoundLine + Environment.NewLine, missing.Out);
        }
        finally
        {
            new CredentialVault().DeleteTarget(VaultNames.HelperTargetName("docker", url));
        }
    }

    [Fact]
    public async Task Deny_prints_the_CmdWarden_line_with_the_reason_and_exits_1()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "off", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        new ToolPinStore(fx.ProductRoot).Save("docker", Path.Combine(Environment.SystemDirectory, "cmd.exe"));

        var (exit, output) = await RunAsync(fx.PipeName, "https://x.example.test/", "get");
        Assert.Equal(1, exit);
        Assert.Equal(
            $"CmdWarden: docker registry credential denied ({PolicyReasonCodes.HelperParentMissing})" + Environment.NewLine,
            output);
    }

    [Fact]
    public async Task Agent_down_prints_a_CmdWarden_line_and_exits_1()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var noAgent = new NoAgentBinary();
        var missingPipe = $"{AgentEndpoints.PipeNamePrefix}-missing-helper-{Guid.NewGuid():N}";
        var (exit, output) = await RunAsync(missingPipe, "https://x.example.test/", "get");
        Assert.Equal(1, exit);
        Assert.StartsWith("CmdWarden: Session Agent not reachable", output);
    }

    [Fact]
    public void Helper_project_builds_an_apphost()
    {
        var helperDir = TestPaths.FindDockerHelperOutputDir();
        Assert.True(File.Exists(Path.Combine(helperDir, HelperTools.DockerHelperExe)));
    }
}
