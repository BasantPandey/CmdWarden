namespace CmdWarden.Contracts;

/// <summary>
/// Per tool × launcher policy. See CONTEXT.md (Policy Level).
/// </summary>
public enum PolicyLevel
{
    Deny = 0,
    Read = 1,
    Trusted = 2,
    Full = 3,
}
