namespace CmdWarden.Contracts;

/// <summary>
/// Coarse classification of a tool invocation. See CONTEXT.md (Command Class).
/// </summary>
public enum CommandClass
{
    Read = 0,
    Write = 1,
    SecretReveal = 2,
    Unknown = 3,
}
