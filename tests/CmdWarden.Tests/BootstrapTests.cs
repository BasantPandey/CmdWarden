using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Bootstrap seam: domain constants and policy enums exist and are stable for later Authorize work.
/// </summary>
public class BootstrapTests
{
    [Fact]
    public void ProductInfo_exposes_CmdWarden_identity_and_cli_names()
    {
        Assert.Equal("CmdWarden", ProductInfo.Name);
        Assert.Equal("cw", ProductInfo.CliPrimary);
        Assert.Equal("cmdwarden", ProductInfo.CliAlias);
        Assert.False(string.IsNullOrWhiteSpace(ProductInfo.Version));
    }

    [Fact]
    public void PolicyLevel_has_Deny_Read_Trusted_Full()
    {
        Assert.Equal(
            new[] { PolicyLevel.Deny, PolicyLevel.Read, PolicyLevel.Trusted, PolicyLevel.Full },
            Enum.GetValues<PolicyLevel>());
    }

    [Fact]
    public void CommandClass_has_Read_Write_SecretReveal_Unknown()
    {
        Assert.Equal(
            new[]
            {
                CommandClass.Read,
                CommandClass.Write,
                CommandClass.SecretReveal,
                CommandClass.Unknown,
            },
            Enum.GetValues<CommandClass>());
    }
}
