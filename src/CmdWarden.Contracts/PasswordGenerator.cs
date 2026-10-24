using System.Security.Cryptography;

namespace CmdWarden.Contracts;

/// <summary>
/// #97: a random password for <c>cw save NAME --generate</c>. It has upper and lower case letters,
/// digits, and symbols. The symbols leave out quotes, slashes, spaces, and angle brackets, so the
/// password also works in a shell and in most sign-up forms.
/// </summary>
public static class PasswordGenerator
{
    public const int DefaultLength = 24;
    public const int MinLength = 12;
    public const int MaxLength = 128;

    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Digits = "0123456789";
    private const string Symbols = "!#$%&*+-.:=?@^_~";
    private const string All = Upper + Lower + Digits + Symbols;

    /// <exception cref="ArgumentOutOfRangeException">The length is not <see cref="MinLength"/> to <see cref="MaxLength"/>.</exception>
    public static char[] New(int length = DefaultLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, MinLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, MaxLength);
        // Draw again until each kind of character is there: this keeps every valid password equally likely.
        while (true)
        {
            var password = RandomNumberGenerator.GetItems<char>(All, length);
            if (password.AsSpan().IndexOfAny(Upper) >= 0 && password.AsSpan().IndexOfAny(Lower) >= 0
                && password.AsSpan().IndexOfAny(Digits) >= 0 && password.AsSpan().IndexOfAny(Symbols) >= 0)
                return password;
            Array.Clear(password);
        }
    }
}
