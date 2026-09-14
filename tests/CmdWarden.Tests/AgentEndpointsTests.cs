using CmdWarden.Contracts;

namespace CmdWarden.Tests;

public class AgentEndpointsTests
{
    [Fact]
    public void PipeName_is_prefixed_and_sanitized()
    {
        var name = AgentEndpoints.PipeName;
        Assert.StartsWith(AgentEndpoints.PipeNamePrefix + "-", name);
        Assert.DoesNotContain(" ", name);
        Assert.DoesNotContain("\\", name);
    }

    [Theory]
    [InlineData("Basant", "Basant")]
    [InlineData("DOMAIN\\user", "DOMAIN_user")]
    [InlineData("", "user")]
    public void Sanitize_strips_unsafe_pipe_characters(string input, string expected)
    {
        Assert.Equal(expected, AgentEndpoints.Sanitize(input));
    }
}
