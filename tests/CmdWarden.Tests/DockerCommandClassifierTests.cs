using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// docker argv → Command Class (issue #42).
/// No first-class secret-reveal subcommands on the docker CLI itself
/// (credential helper get is out of band / residual).
/// </summary>
public class DockerCommandClassifierTests
{
    [Theory]
    [InlineData(new[] { "login" }, CommandClass.Write)]
    [InlineData(new[] { "login", "-u", "user", "-p", "secret" }, CommandClass.Write)]
    [InlineData(new[] { "logout" }, CommandClass.Write)]
    [InlineData(new[] { "push", "myimage:latest" }, CommandClass.Write)]
    [InlineData(new[] { "image", "push", "myimage" }, CommandClass.Write)]
    [InlineData(new[] { "compose", "push" }, CommandClass.Write)]
    [InlineData(new[] { "run", "-it", "alpine" }, CommandClass.Write)]
    [InlineData(new[] { "build", "." }, CommandClass.Write)]
    [InlineData(new[] { "pull", "alpine" }, CommandClass.Read)]
    [InlineData(new[] { "image", "pull", "alpine" }, CommandClass.Read)]
    [InlineData(new[] { "compose", "pull" }, CommandClass.Read)]
    [InlineData(new[] { "ps" }, CommandClass.Read)]
    [InlineData(new[] { "logs", "ctr" }, CommandClass.Read)]
    [InlineData(new[] { "images" }, CommandClass.Read)]
    [InlineData(new[] { "version" }, CommandClass.Read)]
    [InlineData(new[] { "--help" }, CommandClass.Read)]
    [InlineData(new[] { "push", "--help" }, CommandClass.Read)]
    [InlineData(new[] { "totally-unknown-subcommand" }, CommandClass.Unknown)]
    [InlineData(new[] { "xyz", "abc" }, CommandClass.Unknown)]
    public void Classify_maps_expected_classes(string[] argv, CommandClass expected)
    {
        Assert.Equal(expected, DockerCommandClassifier.Classify(argv));
    }

    [Theory]
    [InlineData(new[] { "--help" }, true)]
    [InlineData(new[] { "push", "--help" }, true)]
    [InlineData(new[] { "pull", "alpine" }, false)]
    [InlineData(new[] { "login" }, false)]
    public void IsHelpOnly_detects_meta(string[] argv, bool expected)
    {
        Assert.Equal(expected, DockerCommandClassifier.IsHelpOnly(argv));
    }
}
