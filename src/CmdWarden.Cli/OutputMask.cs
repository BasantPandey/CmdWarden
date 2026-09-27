using System.Text;
using CmdWarden.Contracts;

namespace CmdWarden.Cli;

/// <summary>
/// #69: replaces each released secret value with <c>[CmdWarden: NAME]</c> in a stream of child output.
/// It works on bytes read as Latin-1, so the rest of the output stays byte for byte the same in any
/// code page. It holds back only a tail that could be the start of a secret, so a value split across
/// two reads is still found.
/// </summary>
public sealed class OutputMask
{
    private readonly List<(string Name, string Value)> _secrets;
    private string _pending = "";

    public OutputMask(IEnumerable<KeyValuePair<string, string>> secrets)
    {
        // Same length floor as the leak guard: a shorter value would match ordinary text.
        _secrets = secrets
            .Where(s => s.Value.Length >= LeakRedactor.MinValueLength)
            .Select(s => (s.Key, Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(s.Value))))
            .OrderByDescending(s => s.Item2.Length)
            .ToList();
    }

    /// <summary>The bytes that are safe to write now.</summary>
    public byte[] Push(ReadOnlySpan<byte> chunk)
    {
        var text = Redact(_pending + Encoding.Latin1.GetString(chunk));
        var hold = HeldTail(text);
        _pending = text[^hold..];
        return Encoding.Latin1.GetBytes(text[..^hold]);
    }

    /// <summary>The rest, at the end of the stream.</summary>
    public byte[] Flush()
    {
        var rest = Encoding.Latin1.GetBytes(_pending);
        _pending = "";
        return rest;
    }

    private string Redact(string text)
    {
        foreach (var (name, value) in _secrets)
            text = text.Replace(value, LeakRedactor.Placeholder(name), StringComparison.Ordinal);
        return text;
    }

    /// <summary>The length of the longest end of the text that is the start of a secret value.</summary>
    private int HeldTail(string text)
    {
        var hold = 0;
        foreach (var (_, value) in _secrets)
        {
            for (var n = Math.Min(value.Length - 1, text.Length); n > hold; n--)
            {
                if (text.AsSpan(text.Length - n).SequenceEqual(value.AsSpan(0, n)))
                {
                    hold = n;
                    break;
                }
            }
        }
        return hold;
    }
}
