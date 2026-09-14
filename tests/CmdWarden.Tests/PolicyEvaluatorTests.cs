using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Seam: PolicyEvaluator - pure level x command-class matrix (issue #5).
/// </summary>
public class PolicyEvaluatorTests
{
    [Theory]
    [InlineData(PolicyLevel.Deny, CommandClass.Read, false)]
    [InlineData(PolicyLevel.Deny, CommandClass.Write, false)]
    [InlineData(PolicyLevel.Deny, CommandClass.SecretReveal, false)]
    [InlineData(PolicyLevel.Deny, CommandClass.Unknown, false)]
    [InlineData(PolicyLevel.Read, CommandClass.Read, true)]
    [InlineData(PolicyLevel.Read, CommandClass.Write, false)]
    [InlineData(PolicyLevel.Read, CommandClass.SecretReveal, false)]
    [InlineData(PolicyLevel.Read, CommandClass.Unknown, false)]
    [InlineData(PolicyLevel.Trusted, CommandClass.Read, true)]
    [InlineData(PolicyLevel.Trusted, CommandClass.Write, true)]
    [InlineData(PolicyLevel.Trusted, CommandClass.SecretReveal, false)]
    [InlineData(PolicyLevel.Trusted, CommandClass.Unknown, false)]
    [InlineData(PolicyLevel.Full, CommandClass.Read, true)]
    [InlineData(PolicyLevel.Full, CommandClass.Write, true)]
    [InlineData(PolicyLevel.Full, CommandClass.SecretReveal, true)]
    [InlineData(PolicyLevel.Full, CommandClass.Unknown, true)]
    public void IsAutoAllowed_matches_v1_matrix(PolicyLevel level, CommandClass commandClass, bool expected)
    {
        Assert.Equal(expected, PolicyEvaluator.IsAutoAllowed(level, commandClass));
    }

    [Fact]
    public void Decide_auto_allow_vs_needs_approval()
    {
        Assert.Equal(PolicyDecision.AutoAllow, PolicyEvaluator.Decide(PolicyLevel.Trusted, CommandClass.Write));
        Assert.Equal(PolicyDecision.NeedsApproval, PolicyEvaluator.Decide(PolicyLevel.Trusted, CommandClass.SecretReveal));
        Assert.Equal(PolicyDecision.NeedsApproval, PolicyEvaluator.Decide(PolicyLevel.Deny, CommandClass.Read));
        Assert.Equal(PolicyDecision.AutoAllow, PolicyEvaluator.Decide(PolicyLevel.Full, CommandClass.Unknown));
    }
}
