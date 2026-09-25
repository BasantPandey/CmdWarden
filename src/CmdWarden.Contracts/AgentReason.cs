using System.Globalization;
using System.Text;

namespace CmdWarden.Contracts;

/// <summary>
/// The reason that an AI agent gives for a command (#32). It is a statement of the agent,
/// never proof. The Approval Gate shows it and the audit stores it. Policy never reads it.
/// </summary>
public static class AgentReason
{
    public const string EnvVar = "CW_REASON";
    public const string Label = "The agent says:";
    public const int MaxLength = 200;

    public static string FromEnvironment() => Environment.GetEnvironmentVariable(EnvVar) ?? "";

    /// <summary>
    /// One line of plain text, at most <see cref="MaxLength"/> characters, or null when empty. A cut text ends with an ellipsis.
    /// Control and format characters (new lines, bidi overrides) become spaces, so the agent
    /// cannot change the layout of the card or hide text.
    /// </summary>
    public static string? Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var sb = new StringBuilder(Math.Min(raw.Length, MaxLength));
        var space = false;
        var cut = false;
        foreach (var rune in raw.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            var blank = Rune.IsWhiteSpace(rune) || category is UnicodeCategory.Control or UnicodeCategory.Format
                or UnicodeCategory.Surrogate or UnicodeCategory.OtherNotAssigned
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
            if (blank)
            {
                space = sb.Length > 0;
                continue;
            }
            var extra = (space ? 1 : 0) + rune.Utf16SequenceLength;
            if (sb.Length + extra > MaxLength)
            {
                cut = true;
                break;
            }
            if (space)
                sb.Append(' ');
            sb.Append(rune.ToString());
            space = false;
        }
        if (cut)
        {
            while (sb.Length > MaxLength - 1 || (sb.Length > 0 && sb[^1] == ' '))
                sb.Length -= sb.Length > 1 && char.IsLowSurrogate(sb[^1]) ? 2 : 1;
            sb.Append('…');
        }
        return sb.Length == 0 ? null : sb.ToString();
    }
}
