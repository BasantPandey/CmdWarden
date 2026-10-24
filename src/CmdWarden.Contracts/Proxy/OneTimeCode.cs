using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace CmdWarden.Contracts.Proxy;

/// <summary>
/// #93: the TOTP code (RFC 6238) of a vault entry, for a sign-in key with a one-time code. The
/// entry is the base32 key of the site, or its otpauth://totp/ link with secret, digits, period,
/// and algorithm.
/// </summary>
public static class OneTimeCode
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <exception cref="FormatException">The entry is not a TOTP key or link.</exception>
    public static string From(string secret, DateTimeOffset now)
    {
        var (key, digits, period, algorithm) = Parse(secret.Trim());
        try
        {
            Span<byte> counter = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(counter, now.ToUnixTimeSeconds() / period);
            var hash = algorithm switch
            {
                "SHA256" => HMACSHA256.HashData(key, counter),
                "SHA512" => HMACSHA512.HashData(key, counter),
                _ => HMACSHA1.HashData(key, counter),
            };
            var offset = hash[^1] & 0x0F;
            var binary = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset)) & 0x7FFFFFFF;
            var code = binary % (int)Math.Pow(10, digits);
            return code.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static (byte[] Key, int Digits, int Period, string Algorithm) Parse(string secret)
    {
        if (!secret.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
            return (Base32(secret), 6, 30, "SHA1");
        if (!Uri.TryCreate(secret, UriKind.Absolute, out var uri) || !uri.Host.Equals("totp", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Only otpauth://totp/ links have a time-based code.");
        var query = new Dictionary<string, string>();
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)))
        {
            if (!query.TryAdd(Uri.UnescapeDataString(pair[0]).ToLowerInvariant(), pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : ""))
                throw new FormatException($"The TOTP link names {pair[0]} two times.");
        }
        var key = Base32(query.GetValueOrDefault("secret") ?? "");
        var digits = Number(query, "digits", 6, 6, 8);
        var period = Number(query, "period", 30, 1, 300);
        var algorithm = (query.GetValueOrDefault("algorithm") ?? "SHA1").ToUpperInvariant();
        if (algorithm is not ("SHA1" or "SHA256" or "SHA512"))
            throw new FormatException($"TOTP algorithm {algorithm} is not SHA1, SHA256, or SHA512.");
        return (key, digits, period, algorithm);
    }

    private static int Number(Dictionary<string, string> query, string name, int fallback, int min, int max)
    {
        if (!query.TryGetValue(name, out var text))
            return fallback;
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max
            ? n
            : throw new FormatException($"TOTP {name} must be {min} to {max}.");
    }

    /// <summary>RFC 4648 base32. Spaces, dashes, padding, and case do not matter.</summary>
    private static byte[] Base32(string text)
    {
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in text.ToUpperInvariant())
        {
            if (c is ' ' or '-' or '=')
                continue;
            var value = Alphabet.IndexOf(c);
            if (value < 0)
                throw new FormatException("The TOTP key is not base32.");
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)(buffer >> bits));
                buffer &= (1 << bits) - 1;
            }
        }
        return bytes.Count > 0 ? [.. bytes] : throw new FormatException("The TOTP key is empty.");
    }
}
