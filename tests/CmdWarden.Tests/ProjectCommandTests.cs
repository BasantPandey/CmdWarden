using CmdWarden.Contracts;

namespace CmdWarden.Tests;

public class ProjectCommandTests
{
    [Fact]
    public async Task Save_and_delete_accept_a_project_flag_and_reject_an_unknown_flag()
    {
        var usage = await Capture(() => CliApp.RunAsync(["save"]));
        Assert.Equal(1, usage.Code);
        Assert.Contains("cw save <NAME> [--value <secret>] [--project [folder]]", usage.Out, StringComparison.Ordinal);
        Assert.Contains("nearest folder wins", usage.Out, StringComparison.Ordinal);

        var save = await Capture(() => CliApp.RunAsync(["save", "TOKEN", "--project", @"C:\repo", "--bogus"]));
        Assert.Equal(1, save.Code);
        Assert.Contains("Unknown option for cw save: --bogus", save.Err, StringComparison.Ordinal);

        var bare = await Capture(() => CliApp.RunAsync(["save", "TOKEN", "--project", "--bogus"]));
        Assert.Equal(1, bare.Code);
        Assert.Contains("Unknown option for cw save: --bogus", bare.Err, StringComparison.Ordinal);

        var deleteUsage = await Capture(() => CliApp.RunAsync(["delete"]));
        Assert.Equal(1, deleteUsage.Code);
        Assert.Contains("cw delete <NAME> [--project [folder]]", deleteUsage.Out, StringComparison.Ordinal);

        var delete = await Capture(() => CliApp.RunAsync(["delete", "TOKEN", "--project", @"C:\repo", "--bogus"]));
        Assert.Equal(1, delete.Code);
        Assert.Contains("Unknown option for cw delete: --bogus", delete.Err, StringComparison.Ordinal);

        var deleteBare = await Capture(() => CliApp.RunAsync(["delete", "TOKEN", "--bogus"]));
        Assert.Equal(1, deleteBare.Code);
        Assert.Contains("Unknown option for cw delete: --bogus", deleteBare.Err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Help_names_the_project_flag_and_winget()
    {
        var help = await Capture(() => CliApp.RunAsync(["--help"]));
        Assert.Equal(0, help.Code);
        Assert.Contains("save <NAME> [--project [folder]]", help.Out, StringComparison.Ordinal);
        Assert.Contains("delete <NAME> [--project [folder]]", help.Out, StringComparison.Ordinal);
        Assert.Contains("winget", help.Out, StringComparison.Ordinal);

        var policy = await Capture(() => CliApp.RunAsync(["policy"]));
        Assert.Equal(1, policy.Code);
        Assert.Contains("An agent write asks unless the mode is off.", policy.Out, StringComparison.Ordinal);

        var harden = await Capture(() => CliApp.RunAsync(["harden"]));
        Assert.Equal(1, harden.Code);
        Assert.Contains("kubectl, winget", harden.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hello_says_an_agent_write_asks_unless_the_mode_is_off()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cw-hello-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var previous = Environment.GetEnvironmentVariable("CW_POLICY_PATH");
        Environment.SetEnvironmentVariable("CW_POLICY_PATH", dir);
        try
        {
            var ran = await Capture(() => CliApp.RunAsync(["policy", "hello", "off"]));
            Assert.Equal(0, ran.Code);
            Assert.Contains("An agent write asks for Hello unless the mode is off.", ran.Out, StringComparison.Ordinal);
            Assert.Contains("Windows Hello after Approve: off", ran.Out, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CW_POLICY_PATH", previous);
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task The_client_sets_a_project_path_before_the_call_fails()
    {
        var pipe = "cw-no-agent-" + Guid.NewGuid().ToString("N");
        var timeout = TimeSpan.FromMilliseconds(400);
        await Assert.ThrowsAnyAsync<Exception>(() =>
            AgentVaultClient.SaveAsync("TOKEN", [1], pipeName: pipe, timeout: timeout, projectPath: @"C:\repo"));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            AgentVaultClient.SaveAsync("TOKEN", [1], pipeName: pipe, timeout: timeout, projectPath: " "));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            AgentVaultClient.DeleteAsync("TOKEN", pipeName: pipe, timeout: timeout, projectPath: @"C:\repo"));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            AgentVaultClient.DeleteAsync("TOKEN", pipeName: pipe, timeout: timeout));
    }

    private static Task<(int Code, string Out, string Err)> Capture(Func<Task<int>> run) =>
        CliOutput.Capture(run);
}
