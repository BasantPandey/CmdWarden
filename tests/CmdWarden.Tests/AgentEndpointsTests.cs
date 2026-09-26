using CmdWarden.Contracts;

namespace CmdWarden.Tests;

[Collection("AgentProcess")]
public class AgentEndpointsTests
{
    [Fact]
    public void PipeName_is_prefixed_and_sanitized()
    {
        // Process tests and a dev shell can set CW_PIPE_NAME; this test reads the default name.
        var overrideName = Environment.GetEnvironmentVariable("CW_PIPE_NAME");
        Environment.SetEnvironmentVariable("CW_PIPE_NAME", null);
        try
        {
            var name = AgentEndpoints.PipeName;
            Assert.StartsWith(AgentEndpoints.PipeNamePrefix + "-", name);
            Assert.DoesNotContain(" ", name);
            Assert.DoesNotContain("\\", name);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CW_PIPE_NAME", overrideName);
        }
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
