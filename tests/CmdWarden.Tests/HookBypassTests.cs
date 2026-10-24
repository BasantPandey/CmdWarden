using System.Text;
using CmdWarden.Cli.Hooks;

namespace CmdWarden.Tests;

/// <summary>
/// #97: ways that an agent can wrap a cw command that weakens protection. The policy hook must
/// still find each one. docs/threat-model.md lists these and the ways that stay open.
/// </summary>
public class HookBypassTests
{
    private static string Encoded(string command) => Convert.ToBase64String(Encoding.Unicode.GetBytes(command));

    public static TheoryData<string, string> Wrapped => new()
    {
        { "CW.EXE Policy Set k gh Full", "cw policy set" },
        { "\"cw\" policy set k gh Full", "cw policy set" },
        { @"& 'C:\Users\me\AppData\Local\CmdWarden\app\cw.exe' unharden gh", "cw unharden" },
        { "dotnet C:/x/cw.dll policy set k gh Full", "cw policy set" },
        { @"dotnet exec ""C:\Program Files\CmdWarden\cw.dll"" unharden gh", "cw unharden" },
        { "cmd /c cw policy set k gh Full", "cw policy set" },
        { "cmd.exe /c \"cw unharden gh\"", "cw unharden" },
        { "cmd /s /c \"echo x & cw agent stop\"", "cw agent stop" },
        { "powershell -Command \"cw unharden gh\"", "cw unharden" },
        { "powershell -NoProfile -ExecutionPolicy Bypass cw unharden gh", "cw unharden" },
        { "powershell -enc " + Encoded("cw unharden gh"), "cw unharden" },
        { "pwsh -NoProfile -EncodedCommand " + Encoded("cw agent stop"), "cw agent stop" },
        { "pwsh -c 'cw agent stop'", "cw agent stop" },
        { "bash -c \"cw agent stop\"", "cw agent stop" },
        { "bash -lc 'cd /tmp && cw uninstall'", "cw uninstall" },
        { "sh -c 'cw uninstall'", "cw uninstall" },
        { "Start-Process cw -ArgumentList 'policy set k gh Full'", "cw policy set" },
        { "Start-Process -FilePath C:/app/cw.exe -ArgumentList \"unharden gh\"", "cw unharden" },
        { "cmd /c powershell -c \"bash -c 'cw agent stop'\"", "cw agent stop" },
    };

    [Theory]
    [MemberData(nameof(Wrapped))]
    public void A_wrapped_cw_command_that_weakens_protection_is_found(string command, string expected) =>
        Assert.Contains(expected, PolicyHook.WeakeningCwCalls(command));

    [Theory]
    [InlineData("cmd /c cw read GH_TOKEN")]
    [InlineData("bash -c 'cw read GH_TOKEN | clip'")]
    [InlineData("dotnet C:/x/cw.dll read GH_TOKEN")]
    public void A_wrapped_cw_read_is_found(string command) =>
        Assert.Equal(["cw read"], PolicyHook.RevealCwCalls(command));

    [Theory]
    [InlineData("cmd /c echo hi")]
    [InlineData("bash -c \"ls -la\"")]
    [InlineData("powershell -File script.ps1")]
    [InlineData("pwsh ./build.ps1 policy set")]
    [InlineData("dotnet build CmdWarden.sln")]
    [InlineData("dotnet test tests/CmdWarden.Tests")]
    [InlineData("Start-Process notepad")]
    [InlineData("powershell -c \"Get-ChildItem\"")]
    [InlineData("cmd /c cw policy list")]
    [InlineData("powershell -enc not-base64!")]
    public void A_wrapper_with_no_weakening_cw_command_passes(string command)
    {
        Assert.Empty(PolicyHook.WeakeningCwCalls(command));
        Assert.Empty(PolicyHook.RevealCwCalls(command));
    }

    /// <summary>A renamed copy of cw has no name to match. The threat model names it as open.</summary>
    [Fact]
    public void A_renamed_copy_of_cw_is_not_found() =>
        Assert.Empty(PolicyHook.WeakeningCwCalls("copy cw.exe x.exe; ./x.exe policy set k gh Full"));

    [Fact]
    public void Wrappers_are_read_to_a_depth_of_sixteen()
    {
        string Nest(int levels) => string.Concat(Enumerable.Repeat("cmd /c ", levels)) + "cw uninstall";
        Assert.NotEmpty(PolicyHook.WeakeningCwCalls("cmd /c \"cmd /c \\\"cmd /c cw uninstall\\\"\""));
        Assert.NotEmpty(PolicyHook.WeakeningCwCalls(Nest(PolicyHook.MaxWrapperDepth)));
        Assert.Empty(PolicyHook.WeakeningCwCalls(Nest(PolicyHook.MaxWrapperDepth + 1)));
    }

    [Fact]
    public void A_gated_tool_inside_a_wrapper_is_found() =>
        Assert.Contains(PolicyHook.FindToolCalls("bash -c 'gh auth token'"), c => c.Tool == "gh" && c.Argv.SequenceEqual(["auth", "token"]));
}
