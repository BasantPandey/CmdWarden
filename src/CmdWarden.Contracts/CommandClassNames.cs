namespace CmdWarden.Contracts;

/// <summary>
/// Stable wire/CLI names for <see cref="CommandClass"/>.
/// </summary>
public static class CommandClassNames
{
    public const string Read = "read";
    public const string Write = "write";
    public const string SecretReveal = "secret-reveal";
    public const string Unknown = "unknown";

    public static string Format(CommandClass value) => value switch
    {
        CommandClass.Read => Read,
        CommandClass.Write => Write,
        CommandClass.SecretReveal => SecretReveal,
        CommandClass.Unknown => Unknown,
        _ => Unknown,
    };

    public static bool TryParse(string? text, out CommandClass value)
    {
        value = CommandClass.Unknown;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        switch (text.Trim().ToLowerInvariant())
        {
            case Read:
            case "r":
                value = CommandClass.Read;
                return true;
            case Write:
            case "w":
                value = CommandClass.Write;
                return true;
            case SecretReveal:
            case "secret_reveal":
            case "secretreveal":
            case "sr":
                value = CommandClass.SecretReveal;
                return true;
            case Unknown:
            case "u":
                value = CommandClass.Unknown;
                return true;
            default:
                return false;
        }
    }

    public static CommandClass ParseOrUnknown(string? text) =>
        TryParse(text, out var value) ? value : CommandClass.Unknown;
}
