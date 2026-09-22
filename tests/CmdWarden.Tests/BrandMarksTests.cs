using CmdWarden.Contracts;

namespace CmdWarden.Tests;

public class BrandMarksTests
{
    [Theory]
    [InlineData("gh", "GitHub")]
    [InlineData("git", "Git")]
    [InlineData(" AZ ", "Azure")]
    [InlineData("docker", "Docker")]
    public void Gated_tools_get_their_logo(string tool, string expected) =>
        Assert.Equal(expected, BrandMarks.ForTool(tool)?.Name);

    [Theory]
    [InlineData("inject")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_tools_get_no_logo(string? tool) =>
        Assert.Null(BrandMarks.ForTool(tool));

    [Theory]
    [InlineData("Claude Code", null, "Claude")]
    [InlineData("Cursor", @"C:\Users\me\AppData\Local\Programs\cursor\Cursor.exe", "Cursor")]
    [InlineData("codex.exe", null, "OpenAI")]
    [InlineData("Unknown app", @"C:\Users\me\.local\bin\claude.exe", "Claude")]
    public void Ai_harnesses_get_their_logo(string name, string? path, string expected) =>
        Assert.Equal(expected, BrandMarks.ForLauncher(name, path)?.Name);

    [Theory]
    [InlineData("PowerShell", @"C:\Program Files\PowerShell\7\pwsh.exe")]
    [InlineData("Unknown app", "bad|path<>")]
    public void Other_launchers_get_no_logo(string name, string path) =>
        Assert.Null(BrandMarks.ForLauncher(name, path));
}
