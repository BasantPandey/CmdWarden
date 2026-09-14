namespace CmdWarden.Contracts;

/// <summary>
/// Stable wire/CLI/JSON names for <see cref="PolicyLevel"/>.
/// </summary>
public static class PolicyLevelNames
{
    public const string Deny = "Deny";
    public const string Read = "Read";
    public const string Trusted = "Trusted";
    public const string Full = "Full";

    public static string Format(PolicyLevel value) => value switch
    {
        PolicyLevel.Deny => Deny,
        PolicyLevel.Read => Read,
        PolicyLevel.Trusted => Trusted,
        PolicyLevel.Full => Full,
        _ => Deny,
    };

    public static bool TryParse(string? text, out PolicyLevel value)
    {
        value = PolicyLevel.Deny;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        switch (text.Trim().ToLowerInvariant())
        {
            case "deny":
            case "d":
                value = PolicyLevel.Deny;
                return true;
            case "read":
            case "r":
            case "readonly":
            case "read-only":
                value = PolicyLevel.Read;
                return true;
            case "trusted":
            case "t":
                value = PolicyLevel.Trusted;
                return true;
            case "full":
            case "f":
            case "yolo":
                value = PolicyLevel.Full;
                return true;
            default:
                return false;
        }
    }

    public static PolicyLevel ParseOrDeny(string? text) =>
        TryParse(text, out var value) ? value : PolicyLevel.Deny;
}
