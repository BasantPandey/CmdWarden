using CmdWarden.Cli;

namespace CmdWarden.Tests;

public class InjectRunnerTests
{
    [Fact]
    public void ParseInjectArgs_reads_names_and_command()
    {
        var (names, file, args) = InjectRunner.ParseInjectArgs(
            new[] { "+TOKEN", "+OTHER", "--", "cmd", "/c", "echo", "%TOKEN%" });

        Assert.Equal(new[] { "TOKEN", "OTHER" }, names);
        Assert.Equal("cmd", file);
        Assert.Equal(new[] { "/c", "echo", "%TOKEN%" }, args);
    }

    [Fact]
    public void ParseInjectArgs_requires_separator_and_command()
    {
        Assert.Throws<ArgumentException>(() => InjectRunner.ParseInjectArgs(new[] { "+TOKEN" }));
        Assert.Throws<ArgumentException>(() => InjectRunner.ParseInjectArgs(new[] { "--", "cmd" }));
    }
}
