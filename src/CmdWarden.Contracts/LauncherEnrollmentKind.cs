namespace CmdWarden.Contracts;

/// <summary>
/// Product enrollment classification of a launcher (not identity verification kind).
/// Defaults: AI Harness → Read, Terminal → Trusted (issue #5 / CONTEXT.md).
/// </summary>
public enum LauncherEnrollmentKind
{
    /// <summary>Not enrolled or unclassified - resolves to Deny.</summary>
    Unknown = 0,

    /// <summary>AI coding harness (Cursor, Claude Code, …). Default policy: Read.</summary>
    AiHarness = 1,

    /// <summary>Interactive terminal / shell. Default policy: Trusted.</summary>
    Terminal = 2,
}

/// <summary>
/// Wire/JSON names for <see cref="LauncherEnrollmentKind"/>.
/// </summary>
public static class LauncherEnrollmentKindNames
{
    public const string Unknown = "unknown";
    public const string AiHarness = "ai_harness";
    public const string Terminal = "terminal";

    public static string Format(LauncherEnrollmentKind value) => value switch
    {
        LauncherEnrollmentKind.AiHarness => AiHarness,
        LauncherEnrollmentKind.Terminal => Terminal,
        _ => Unknown,
    };

    public static bool TryParse(string? text, out LauncherEnrollmentKind value)
    {
        value = LauncherEnrollmentKind.Unknown;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        switch (text.Trim().ToLowerInvariant())
        {
            case "ai_harness":
            case "ai-harness":
            case "aiharness":
            case "harness":
            case "ai":
                value = LauncherEnrollmentKind.AiHarness;
                return true;
            case "terminal":
            case "term":
            case "shell":
                value = LauncherEnrollmentKind.Terminal;
                return true;
            case "unknown":
                value = LauncherEnrollmentKind.Unknown;
                return true;
            default:
                return false;
        }
    }
}
