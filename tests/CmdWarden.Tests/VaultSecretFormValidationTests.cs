using System.Security.Cryptography;
using System.Text;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

public class VaultSecretFormValidationTests
{
    [Fact]
    public void Valid_name_and_value_succeeds_and_normalizes()
    {
        var result = VaultSecretFormValidation.Validate("  +DEMO_TOKEN  ", "secret-value");
        try
        {
            Assert.True(result.Ok);
            Assert.Equal("DEMO_TOKEN", result.NormalizedName);
            Assert.Null(result.NameError);
            Assert.Null(result.ValueError);
            Assert.NotNull(result.ValueBytes);
            Assert.Equal(Encoding.UTF8.GetBytes("secret-value"), result.ValueBytes);
        }
        finally
        {
            if (result.ValueBytes is not null)
                CryptographicOperations.ZeroMemory(result.ValueBytes);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_name_fails(string? name)
    {
        var result = VaultSecretFormValidation.Validate(name, "x");
        Assert.False(result.Ok);
        Assert.Equal("Name is required.", result.NameError);
        Assert.Null(result.ValueBytes);
    }

    [Fact]
    public void Invalid_name_characters_fail_with_VaultNames_message()
    {
        var result = VaultSecretFormValidation.Validate("bad name!", "x");
        Assert.False(result.Ok);
        Assert.NotNull(result.NameError);
        Assert.Contains("letters", result.NameError, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.ValueBytes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_value_fails(string? value)
    {
        var result = VaultSecretFormValidation.Validate("OK_NAME", value);
        Assert.False(result.Ok);
        Assert.Equal("Value is required.", result.ValueError);
        Assert.Null(result.ValueBytes);
    }

    [Fact]
    public void Value_over_credman_limit_fails()
    {
        var tooBig = new string('a', VaultSecretFormValidation.MaxValueBytes + 1);
        var result = VaultSecretFormValidation.Validate("OK_NAME", tooBig);
        Assert.False(result.Ok);
        Assert.Contains("2560", result.ValueError);
        Assert.Null(result.ValueBytes);
    }

    [Fact]
    public void Value_at_credman_limit_succeeds()
    {
        var exact = new string('b', VaultSecretFormValidation.MaxValueBytes);
        var result = VaultSecretFormValidation.Validate("OK_NAME", exact);
        try
        {
            Assert.True(result.Ok);
            Assert.Equal(VaultSecretFormValidation.MaxValueBytes, result.ValueBytes!.Length);
        }
        finally
        {
            if (result.ValueBytes is not null)
                CryptographicOperations.ZeroMemory(result.ValueBytes);
        }
    }

    [Fact]
    public void RequiresReplaceConfirm_when_name_already_listed()
    {
        Assert.True(VaultSecretFormValidation.RequiresReplaceConfirm(
            "GH_TOKEN",
            new[] { "DEMO", "GH_TOKEN", "OTHER" }));
        Assert.True(VaultSecretFormValidation.RequiresReplaceConfirm(
            "GH_TOKEN",
            new[] { "  +GH_TOKEN  " }));
        Assert.False(VaultSecretFormValidation.RequiresReplaceConfirm(
            "GH_TOKEN",
            new[] { "DEMO", "OTHER" }));
    }

    [Fact]
    public void SanitizeError_truncates_long_messages_without_leaking_shape_issues()
    {
        var longMsg = new string('x', 200);
        var sanitized = VaultSecretFormValidation.SanitizeError(longMsg);
        Assert.Equal(183, sanitized.Length); // 180 + "..."
        Assert.EndsWith("...", sanitized);
    }
}
