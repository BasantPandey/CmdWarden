using System.Globalization;
using System.Text;

namespace CmdWarden.Contracts.Proxy;

/// <summary>The request line and headers of one HTTP/1.1 request.</summary>
public sealed class HttpRequestHead
{
    public string Method { get; set; } = "";
    public string Target { get; set; } = "";
    public string Version { get; set; } = "HTTP/1.1";
    public List<KeyValuePair<string, string>> Headers { get; } = [];

    public string? Header(string name) =>
        Headers.FirstOrDefault(h => h.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    public long? ContentLength =>
        long.TryParse(Header("Content-Length"), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

    public bool IsChunked =>
        Header("Transfer-Encoding")?.Contains("chunked", StringComparison.OrdinalIgnoreCase) == true;

    public bool IsUpgrade => Header("Upgrade") is not null;

    /// <summary>The target and every header value: where a placeholder can be.</summary>
    public IEnumerable<string> Texts => Headers.Select(h => h.Value).Prepend(Target);

    /// <summary>Apply <paramref name="map"/> to the target and to every header value.</summary>
    public void Rewrite(Func<string, string> map)
    {
        Target = map(Target);
        for (var i = 0; i < Headers.Count; i++)
            Headers[i] = new(Headers[i].Key, map(Headers[i].Value));
    }

    public byte[] ToBytes()
    {
        var text = new StringBuilder().Append(Method).Append(' ').Append(Target).Append(' ').Append(Version).Append("\r\n");
        foreach (var (name, value) in Headers)
            text.Append(name).Append(": ").Append(value).Append("\r\n");
        return Encoding.Latin1.GetBytes(text.Append("\r\n").ToString());
    }

    internal static HttpRequestHead Parse(string text)
    {
        var lines = text.Split("\r\n");
        var parts = lines[0].Split(' ');
        if (parts.Length != 3 || parts[0].Length == 0 || !parts[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
            throw new InvalidDataException("Bad HTTP request line.");
        var head = new HttpRequestHead { Method = parts[0], Target = parts[1], Version = parts[2] };
        foreach (var line in lines.Skip(1).Where(l => l.Length > 0))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
                throw new InvalidDataException("Bad HTTP header line.");
            head.Headers.Add(new(line[..colon].Trim(), line[(colon + 1)..].Trim()));
        }
        return head;
    }
}

/// <summary>
/// Reads HTTP/1.1 requests from a stream and copies their bodies as they are. It never reads past
/// the end of a request, so the next request, or the raw bytes after an Upgrade, stay intact.
/// </summary>
public sealed class HttpReader(Stream inner)
{
    public const int MaxHead = 64 * 1024;
    private byte[] _buffer = new byte[16 * 1024];
    private int _start;
    private int _end;

    /// <summary>The next request head, or null when the stream ends between requests.</summary>
    public async Task<HttpRequestHead?> ReadHeadAsync(CancellationToken ct)
    {
        while (true)
        {
            var at = IndexOf("\r\n\r\n"u8);
            if (at >= 0)
            {
                var text = Encoding.Latin1.GetString(_buffer, _start, at - _start);
                _start = at + 4;
                return HttpRequestHead.Parse(text);
            }
            if (_end - _start >= MaxHead)
                throw new InvalidDataException("HTTP request head is too long.");
            if (!await FillAsync(ct).ConfigureAwait(false))
                return _end == _start ? null : throw new EndOfStreamException("HTTP request head ends early.");
        }
    }

    /// <summary>Copy exactly <paramref name="count"/> body bytes.</summary>
    public async Task CopyAsync(Stream destination, long count, CancellationToken ct)
    {
        while (count > 0)
        {
            if (_start == _end && !await FillAsync(ct).ConfigureAwait(false))
                throw new EndOfStreamException("HTTP body ends early.");
            var n = (int)Math.Min(count, _end - _start);
            await destination.WriteAsync(_buffer.AsMemory(_start, n), ct).ConfigureAwait(false);
            _start += n;
            count -= n;
        }
    }

    /// <summary>Copy a chunked body with its chunk lines and trailers, as it is.</summary>
    public async Task CopyChunkedAsync(Stream destination, CancellationToken ct)
    {
        while (true)
        {
            var line = await ReadLineAsync(ct).ConfigureAwait(false);
            await destination.WriteAsync(Encoding.Latin1.GetBytes(line + "\r\n"), ct).ConfigureAwait(false);
            var sizeText = line.Split(';')[0].Trim();
            if (!long.TryParse(sizeText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var size) || size < 0)
                throw new InvalidDataException("Bad chunk size.");
            if (size == 0)
                break;
            await CopyAsync(destination, size + 2, ct).ConfigureAwait(false);
        }
        while (true)
        {
            var trailer = await ReadLineAsync(ct).ConfigureAwait(false);
            await destination.WriteAsync(Encoding.Latin1.GetBytes(trailer + "\r\n"), ct).ConfigureAwait(false);
            if (trailer.Length == 0)
                return;
        }
    }

    /// <summary>Copy only what is left in the buffer, for example TLS bytes that came with a CONNECT.</summary>
    public async Task CopyBufferedAsync(Stream destination, CancellationToken ct)
    {
        if (_end > _start)
            await destination.WriteAsync(_buffer.AsMemory(_start, _end - _start), ct).ConfigureAwait(false);
        _start = _end = 0;
    }

    /// <summary>Copy what is left in the buffer, then everything the stream sends until it ends.</summary>
    public async Task CopyRestAsync(Stream destination, CancellationToken ct)
    {
        if (_end > _start)
            await destination.WriteAsync(_buffer.AsMemory(_start, _end - _start), ct).ConfigureAwait(false);
        _start = _end = 0;
        await inner.CopyToAsync(destination, ct).ConfigureAwait(false);
    }

    private async Task<string> ReadLineAsync(CancellationToken ct)
    {
        while (true)
        {
            var at = IndexOf("\r\n"u8);
            if (at >= 0)
            {
                var line = Encoding.Latin1.GetString(_buffer, _start, at - _start);
                _start = at + 2;
                return line;
            }
            if (_end - _start >= MaxHead)
                throw new InvalidDataException("HTTP line is too long.");
            if (!await FillAsync(ct).ConfigureAwait(false))
                throw new EndOfStreamException("HTTP body ends early.");
        }
    }

    private int IndexOf(ReadOnlySpan<byte> value)
    {
        var at = _buffer.AsSpan(_start, _end - _start).IndexOf(value);
        return at < 0 ? -1 : _start + at;
    }

    private async Task<bool> FillAsync(CancellationToken ct)
    {
        if (_start > 0)
        {
            Buffer.BlockCopy(_buffer, _start, _buffer, 0, _end - _start);
            _end -= _start;
            _start = 0;
        }
        if (_end == _buffer.Length)
            Array.Resize(ref _buffer, Math.Min(_buffer.Length * 2, MaxHead * 2));
        var n = await inner.ReadAsync(_buffer.AsMemory(_end), ct).ConfigureAwait(false);
        _end += n;
        return n > 0;
    }
}
