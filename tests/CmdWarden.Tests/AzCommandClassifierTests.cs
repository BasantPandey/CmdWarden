using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// az argv → Command Class (issue #41).
/// </summary>
public class AzCommandClassifierTests
{
    [Theory]
    [InlineData(new[] { "account", "get-access-token" }, CommandClass.SecretReveal)]
    [InlineData(new[] { "account", "get-access-token", "--query", "accessToken", "-o", "tsv" }, CommandClass.SecretReveal)]
    [InlineData(new[] { "ad", "sp", "create-for-rbac" }, CommandClass.SecretReveal)]
    [InlineData(new[] { "keyvault", "secret", "show", "--name", "x", "--vault-name", "v" }, CommandClass.SecretReveal)]
    [InlineData(new[] { "storage", "account", "keys", "list", "-n", "acct" }, CommandClass.SecretReveal)]
    [InlineData(new[] { "login" }, CommandClass.Write)]
    [InlineData(new[] { "logout" }, CommandClass.Write)]
    [InlineData(new[] { "account", "clear" }, CommandClass.Write)]
    [InlineData(new[] { "group", "create", "-n", "rg", "-l", "eastus" }, CommandClass.Write)]
    [InlineData(new[] { "vm", "create", "-g", "rg", "-n", "vm1" }, CommandClass.Write)]
    [InlineData(new[] { "role", "assignment", "create" }, CommandClass.Write)]
    [InlineData(new[] { "group", "delete", "-n", "rg", "--yes" }, CommandClass.Write)]
    [InlineData(new[] { "account", "show" }, CommandClass.Read)]
    [InlineData(new[] { "account", "list" }, CommandClass.Read)]
    [InlineData(new[] { "group", "list" }, CommandClass.Read)]
    [InlineData(new[] { "vm", "show", "-g", "rg", "-n", "vm1" }, CommandClass.Read)]
    [InlineData(new[] { "--output", "json", "group", "list" }, CommandClass.Read)]
    [InlineData(new[] { "--help" }, CommandClass.Read)]
    [InlineData(new[] { "version" }, CommandClass.Read)]
    [InlineData(new[] { "group", "create", "--help" }, CommandClass.Read)]
    [InlineData(new[] { "totally-unknown-subcommand" }, CommandClass.Unknown)]
    [InlineData(new[] { "xyz", "abc" }, CommandClass.Unknown)]
    public void Classify_maps_expected_classes(string[] argv, CommandClass expected)
    {
        Assert.Equal(expected, AzCommandClassifier.Classify(argv));
    }

    [Theory]
    [InlineData(new[] { "--help" }, true)]
    [InlineData(new[] { "group", "create", "--help" }, true)]
    [InlineData(new[] { "account", "list" }, false)]
    [InlineData(new[] { "account", "get-access-token" }, false)]
    public void IsHelpOnly_detects_meta(string[] argv, bool expected)
    {
        Assert.Equal(expected, AzCommandClassifier.IsHelpOnly(argv));
    }
}
