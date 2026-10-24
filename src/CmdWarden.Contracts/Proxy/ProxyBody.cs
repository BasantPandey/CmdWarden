using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CmdWarden.Contracts.Proxy;

/// <summary>A body field whose whole value is cw://NAME.</summary>
public sealed record BodyPlaceholder(string Field, string Name);

/// <summary>The placeholders in a body: whole field values, and the names found anywhere else.</summary>
public sealed record BodyScan(IReadOnlyList<BodyPlaceholder> Fields, IReadOnlyList<string> Loose);

/// <summary>
/// #85: placeholders in a sign-in body, so a browser can post cw://NAME and the site gets the value.
/// Only a field whose whole value is the placeholder gets the value. Form (urlencoded) and JSON
/// bodies only; a placeholder in another body stays loose.
/// </summary>
public static partial class ProxyBody
{
    public const int MaxLength = 1024 * 1024;

    [GeneratedRegex(@"^cw://([A-Za-z0-9_.\-]+)$")]
    private static partial Regex Whole();

    private enum Kind { Other, Form, Json }

    /// <summary>Field is null for a loose placeholder. Start and Length give the bytes of the value.</summary>
    private sealed record Hit(string? Field, string Name, int Start, int Length);

    public static BodyScan Scan(byte[] body, string? contentType)
    {
        var hits = Walk(body, KindOf(contentType));
        return new BodyScan(
            hits.Where(h => h.Field is not null).Select(h => new BodyPlaceholder(h.Field!, h.Name)).ToList(),
            hits.Where(h => h.Field is null).Select(h => h.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>The body with each field placeholder that <paramref name="values"/> names replaced by its value.</summary>
    public static byte[] Fill(byte[] body, string? contentType, IReadOnlyDictionary<string, string> values)
    {
        var kind = KindOf(contentType);
        var output = new MemoryStream();
        var at = 0;
        foreach (var hit in Walk(body, kind).Where(h => h.Field is not null && values.ContainsKey(h.Name)))
        {
            output.Write(body, at, hit.Start - at);
            var value = values[hit.Name];
            output.Write(kind == Kind.Form
                ? Encoding.ASCII.GetBytes(Uri.EscapeDataString(value))
                : [(byte)'"', .. JsonEncodedText.Encode(value).EncodedUtf8Bytes, (byte)'"']);
            at = hit.Start + hit.Length;
        }
        output.Write(body, at, body.Length - at);
        return output.ToArray();
    }

    private static Kind KindOf(string? contentType)
    {
        var media = (contentType ?? "").Split(';')[0].Trim().ToLowerInvariant();
        return media == "application/x-www-form-urlencoded" ? Kind.Form
            : media == "application/json" || media.EndsWith("+json", StringComparison.Ordinal) ? Kind.Json
            : Kind.Other;
    }

    private static List<Hit> Walk(byte[] body, Kind kind)
    {
        var hits = new List<Hit>();
        if (kind == Kind.Form)
        {
            var text = Encoding.Latin1.GetString(body);
            var at = 0;
            foreach (var pair in text.Split('&'))
            {
                var eq = pair.IndexOf('=');
                var name = Decode(eq < 0 ? pair : pair[..eq]);
                var value = eq < 0 ? "" : Decode(pair[(eq + 1)..]);
                AddLoose(hits, name);
                if (eq >= 0 && Whole().Match(value) is { Success: true } m)
                    hits.Add(new Hit(name, m.Groups[1].Value, at + eq + 1, pair.Length - eq - 1));
                else
                    AddLoose(hits, value);
                at += pair.Length + 1;
            }
        }
        else if (kind == Kind.Json)
        {
            try
            {
                var reader = new Utf8JsonReader(body);
                string? property = null;
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        property = reader.GetString()!;
                        AddLoose(hits, property);
                        continue;
                    }
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        var value = reader.GetString()!;
                        // The token starts at its opening quote; ValueSpan is the raw text between the quotes.
                        if (property is not null && Whole().Match(value) is { Success: true } m)
                            hits.Add(new Hit(property, m.Groups[1].Value, (int)reader.TokenStartIndex, reader.ValueSpan.Length + 2));
                        else
                            AddLoose(hits, value);
                    }
                    property = null;
                }
            }
            // GetString throws InvalidOperationException for a string that is not UTF-8 (#97).
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                hits.Clear();
                AddLoose(hits, Encoding.UTF8.GetString(body));
            }
        }
        else
        {
            AddLoose(hits, Encoding.Latin1.GetString(body));
        }
        return hits;
    }

    private static string Decode(string text) => Uri.UnescapeDataString(text.Replace('+', ' '));

    private static void AddLoose(List<Hit> hits, string text) =>
        hits.AddRange(KeyProxy.Placeholder().Matches(text).Select(m => new Hit(null, m.Groups[1].Value, 0, 0)));
}
