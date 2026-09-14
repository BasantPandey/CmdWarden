using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// git argv → Command Class (issue #40).
/// </summary>
public class GitCommandClassifierTests
{
    [Theory]
    [InlineData(new[] { "credential", "fill" }, CommandClass.SecretReveal)]
    [InlineData(new[] { "credential", "get" }, CommandClass.SecretReveal)]
    [InlineData(new[] { "git", "credential", "fill" }, CommandClass.SecretReveal)]
    [InlineData(new[] { "credential-manager", "get" }, CommandClass.SecretReveal)]
    [InlineData(new[] { "credential-manager-core", "get" }, CommandClass.SecretReveal)]
    [InlineData(new[] { "credential-store", "get" }, CommandClass.SecretReveal)]
    [InlineData(new[] { "credential-wincred", "get" }, CommandClass.SecretReveal)]
    [InlineData(new[] { "credential-cache", "get" }, CommandClass.SecretReveal)]
    [InlineData(new[] { "credential-foo", "store" }, CommandClass.SecretReveal)]
    [InlineData(new[] { "-c", "credential.helper=!evil", "status" }, CommandClass.SecretReveal)]
    [InlineData(new[] { "-c", "http.extraheader=Authorization: bearer x", "fetch" }, CommandClass.SecretReveal)]
    [InlineData(new[] { "-c", "core.askpass=steal.exe", "status" }, CommandClass.SecretReveal)]
    [InlineData(new[] { "-ccore.askpass=steal.exe", "fetch" }, CommandClass.SecretReveal)]
    [InlineData(new[] { "--config-env=core.askpass=ASKPASS", "status" }, CommandClass.SecretReveal)]
    [InlineData(new[] { "push" }, CommandClass.Write)]
    [InlineData(new[] { "push", "origin", "main" }, CommandClass.Write)]
    [InlineData(new[] { "-C", "repo", "push", "--force" }, CommandClass.Write)]
    [InlineData(new[] { "commit", "-m", "x" }, CommandClass.Write)]
    [InlineData(new[] { "add", "." }, CommandClass.Write)]
    [InlineData(new[] { "credential", "approve" }, CommandClass.Write)]
    [InlineData(new[] { "config", "--global", "credential.helper", "store" }, CommandClass.Write)]
    [InlineData(new[] { "remote", "set-url", "origin", "https://x:y@github.com/a/b.git" }, CommandClass.Write)]
    [InlineData(new[] { "fetch" }, CommandClass.Read)]
    [InlineData(new[] { "pull" }, CommandClass.Read)]
    [InlineData(new[] { "clone", "https://example.com/r.git" }, CommandClass.Read)]
    [InlineData(new[] { "status" }, CommandClass.Read)]
    [InlineData(new[] { "log", "--oneline" }, CommandClass.Read)]
    [InlineData(new[] { "diff" }, CommandClass.Read)]
    [InlineData(new[] { "ls-remote", "origin" }, CommandClass.Read)]
    [InlineData(new[] { "--help" }, CommandClass.Read)]
    [InlineData(new[] { "version" }, CommandClass.Read)]
    [InlineData(new[] { "push", "--help" }, CommandClass.Read)]
    [InlineData(new[] { "config", "--get", "user.name" }, CommandClass.Read)]
    [InlineData(new[] { "config", "user.name" }, CommandClass.Read)]
    [InlineData(new[] { "totally-unknown-subcommand" }, CommandClass.Unknown)]
    [InlineData(new[] { "xyz", "abc" }, CommandClass.Unknown)]
    public void Classify_maps_expected_classes(string[] argv, CommandClass expected)
    {
        Assert.Equal(expected, GitCommandClassifier.Classify(argv));
    }

    [Theory]
    [InlineData(new[] { "--help" }, true)]
    [InlineData(new[] { "push", "--help" }, true)]
    [InlineData(new[] { "status" }, false)]
    [InlineData(new[] { "credential", "fill" }, false)]
    public void IsHelpOnly_detects_meta(string[] argv, bool expected)
    {
        Assert.Equal(expected, GitCommandClassifier.IsHelpOnly(argv));
    }
}
