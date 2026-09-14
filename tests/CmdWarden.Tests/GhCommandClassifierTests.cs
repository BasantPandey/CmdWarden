using CmdWarden.Contracts;

namespace CmdWarden.Tests;

public class GhCommandClassifierTests
{
    [Theory]
    [InlineData(new[] { "pr", "list" }, CommandClass.Read)]
    [InlineData(new[] { "pr", "view", "1" }, CommandClass.Read)]
    [InlineData(new[] { "repo", "view" }, CommandClass.Read)]
    [InlineData(new[] { "issue", "list" }, CommandClass.Read)]
    [InlineData(new[] { "auth", "status" }, CommandClass.Read)]
    [InlineData(new[] { "--help" }, CommandClass.Read)]
    [InlineData(new[] { "version" }, CommandClass.Read)]
    [InlineData(new[] { "pr", "create", "--help" }, CommandClass.Read)]
    [InlineData(new[] { "auth", "login", "-h" }, CommandClass.Read)]
    [InlineData(new[] { "auth", "token" }, CommandClass.SecretReveal)]
    [InlineData(new[] { "auth", "status", "--show-token" }, CommandClass.SecretReveal)]
    [InlineData(new[] { "auth", "status", "-h", "github.com", "--show-token" }, CommandClass.SecretReveal)]
    [InlineData(new[] { "--show-token", "auth", "status" }, CommandClass.SecretReveal)]
    [InlineData(new[] { "auth", "git-credential", "get" }, CommandClass.SecretReveal)]
    [InlineData(new[] { "pr", "create" }, CommandClass.Write)]
    [InlineData(new[] { "issue", "comment", "1", "--body", "x" }, CommandClass.Write)]
    [InlineData(new[] { "repo", "create" }, CommandClass.Write)]
    [InlineData(new[] { "release", "create", "v1" }, CommandClass.Write)]
    [InlineData(new[] { "workflow", "run", "ci.yml" }, CommandClass.Write)]
    [InlineData(new[] { "auth", "login" }, CommandClass.Write)]
    [InlineData(new[] { "api", "user" }, CommandClass.Write)]
    [InlineData(new[] { "totally-unknown-subcommand" }, CommandClass.Unknown)]
    [InlineData(new[] { "pr" }, CommandClass.Unknown)]
    [InlineData(new[] { "xyz", "abc" }, CommandClass.Unknown)]
    public void Classify_maps_expected_classes(string[] argv, CommandClass expected)
    {
        Assert.Equal(expected, GhCommandClassifier.Classify(argv));
    }

    [Theory]
    [InlineData(new[] { "--help" }, true)]
    [InlineData(new[] { "pr", "create", "--help" }, true)]
    [InlineData(new[] { "pr", "list" }, false)]
    [InlineData(new[] { "auth", "token" }, false)]
    public void IsHelpOnly_detects_meta(string[] argv, bool expected)
    {
        Assert.Equal(expected, GhCommandClassifier.IsHelpOnly(argv));
    }
}
