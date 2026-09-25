using System.Security.Cryptography;
using System.Text;

namespace CmdWarden.Contracts;

/// <summary>One secret value the leak guard looks for. Lives in the Agent only.</summary>
public sealed record KnownSecret(string Name, string Value, bool IsCanary = false);

/// <summary>
/// Leak guard (#27): replace each exact vaulted value in a text with <c>[CmdWarden: NAME]</c>.
/// CmdWarden holds the real values, so a match is never a guess.
/// </summary>
public static class LeakRedactor
{
    /// <summary>
    /// A shorter value would match ordinary text.
    /// ponytail: exact substring only; add base64 / URL-encoded forms when a real leak needs them.
    /// </summary>
    public const int MinValueLength = 8;

    public static string Placeholder(string name) => $"[{ProductInfo.Name}: {name}]";

    public sealed record Result(IReadOnlyList<string> Texts, IReadOnlyList<KnownSecret> Matches);

    public static Result Redact(IReadOnlyList<string> texts, IEnumerable<KnownSecret> secrets)
    {
        // Longest first, so a value that holds another value is replaced as a whole.
        var ordered = secrets
            .Where(s => s.Value.Length >= MinValueLength)
            .OrderByDescending(s => s.Value.Length)
            .ToList();
        var matches = new List<KnownSecret>();
        var output = new List<string>(texts.Count);
        foreach (var text in texts)
        {
            var current = text ?? "";
            foreach (var secret in ordered)
            {
                if (!current.Contains(secret.Value, StringComparison.Ordinal))
                    continue;
                current = current.Replace(secret.Value, Placeholder(secret.Name), StringComparison.Ordinal);
                if (!matches.Contains(secret))
                    matches.Add(secret);
            }
            output.Add(current);
        }
        return new Result(output, matches);
    }
}

/// <summary>SHA-256 of a value, so a caller can show a value to the Agent without sending it (#29).</summary>
public static class ValueHash
{
    public static string Of(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>Hashes of this process's env values that are long enough to be a token.</summary>
    public static IEnumerable<string> OfEnvironment()
    {
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
        {
            if (e.Value?.ToString() is { Length: >= LeakRedactor.MinValueLength } value)
                yield return Of(value);
        }
    }
}
