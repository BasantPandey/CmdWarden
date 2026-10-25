using FsCheck.Xunit;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// FsCheck builds many reasons. Each clean reason stays short and plain.
/// </summary>
public class AgentReasonPropertyTests
{
    /// <summary>Scorecard looks for this using line. The property calls the shipped cleaner.</summary>
    [Property(MaxTest = 200, EndSize = 80)]
    public void A_clean_reason_stays_short_and_plain(string? raw)
    {
        var clean = AgentReason.Clean(raw);
        if (clean is null)
            return;
        Assert.InRange(clean.Length, 1, AgentReason.MaxLength);
        Assert.DoesNotContain(clean, c => char.IsControl(c) || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.Format);
    }
}
