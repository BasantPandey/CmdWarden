using System.Text;
using CmdWarden.Cli;
using CmdWarden.Cli.Hooks;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Proxy;

namespace CmdWarden.Tests;

/// <summary>
/// #97: the parsers that read data an agent controls get many changed copies of good input. Each
/// parser must end, throw only its own errors, and keep its limits. The seed makes a failure
/// repeat: the message names the input. Set CW_FUZZ_ITERATIONS for a longer run, and CW_FUZZ_SEED
/// for other inputs.
/// ponytail: random changes, not coverage-guided fuzzing. SharpFuzz with libFuzzer finds more.
/// </summary>
public class FuzzTests
{
    private static readonly int Iterations =
        int.TryParse(Environment.GetEnvironmentVariable("CW_FUZZ_ITERATIONS"), out var n) && n > 0 ? n : 2000;

    private static readonly int SeedOffset =
        int.TryParse(Environment.GetEnvironmentVariable("CW_FUZZ_SEED"), out var s) ? s : 0;

    private static readonly string[] Tokens =
    [
        "\r\n", "\r\n\r\n", "\n", "\0", ":", ";", "=", "&", "%", "%0d%0a", "+", "\"", "'", "\\", "{", "}", "[", "]", ",",
        "cw://A", "cw://", "chunked", "Content-Length: ", "Transfer-Encoding: ", "0", "-1", "ffffffffffffffff",
        "7fffffffffffffff", "99999999999999999999", "\u202e", "\u2028", "\ud800", "export ", "#", "cmd /c ", "bash -c ",
        "powershell -enc ", "Start-Process ", "dotnet cw.dll ", "cw policy set ", "\"cw unharden gh\"",
    ];

    /// <summary>A copy of one seed with a few random changes: flip, insert, delete, repeat, or a known token.</summary>
    private static byte[] Mutate(Random random, IReadOnlyList<byte[]> seeds)
    {
        var data = new List<byte>(seeds[random.Next(seeds.Count)]);
        for (var k = random.Next(1, 6); k > 0; k--)
        {
            var at = data.Count == 0 ? 0 : random.Next(data.Count + 1);
            switch (random.Next(6))
            {
                case 0 when data.Count > 0:
                    data[Math.Min(at, data.Count - 1)] ^= (byte)(1 << random.Next(8));
                    break;
                case 1:
                    data.Insert(at, (byte)random.Next(256));
                    break;
                case 2 when data.Count > 0:
                    data.RemoveRange(Math.Min(at, data.Count - 1), Math.Min(random.Next(1, 16), data.Count - Math.Min(at, data.Count - 1)));
                    break;
                case 3 when data.Count > 0:
                    var from = random.Next(data.Count);
                    data.InsertRange(at, data.Skip(from).Take(random.Next(1, 64)).ToList());
                    break;
                default:
                    data.InsertRange(at, Encoding.UTF8.GetBytes(Tokens[random.Next(Tokens.Length)]));
                    break;
            }
        }
        return [.. data];
    }

    private static void Fuzz(int seed, IReadOnlyList<string> seeds, Action<byte[]> check)
    {
        seed += SeedOffset;
        var random = new Random(seed);
        var corpus = seeds.Select(s => Encoding.UTF8.GetBytes(s)).ToList();
        for (var i = 0; i < Iterations; i++)
        {
            var input = Mutate(random, corpus);
            try
            {
                check(input);
            }
            catch (Exception ex)
            {
                throw new Xunit.Sdk.XunitException(
                    $"seed {seed}, iteration {i}, input (base64) {Convert.ToBase64String(input)}: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>A stream that gives its bytes in small random reads, so a request ends at any buffer edge.</summary>
    private sealed class Trickle(byte[] data, int seed) : MemoryStream(data)
    {
        private readonly Random _random = new(seed);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, _random.Next(1, 40))], ct);
    }

    private static readonly string[] HttpSeeds =
    [
        "GET /v1/models HTTP/1.1\r\nHost: api.openai.com\r\nAuthorization: Bearer cw://OPENAI_API_KEY\r\n\r\n",
        "POST /session HTTP/1.1\r\nHost: github.com\r\nContent-Type: application/x-www-form-urlencoded\r\nContent-Length: 34\r\n\r\nlogin=me&password=cw%3A%2F%2FPW&x=1",
        "POST /api HTTP/1.1\r\nHost: x\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhello\r\n6;ext=1\r\n world\r\n0\r\nTrailer: t\r\n\r\n",
        "GET / HTTP/1.1\r\nHost: a\r\n\r\nGET /2 HTTP/1.1\r\nHost: a\r\n\r\n",
    ];

    [Fact]
    public void The_http_reader_ends_and_throws_only_its_own_errors()
    {
        Fuzz(41, HttpSeeds, input =>
        {
            var reader = new HttpReader(new Trickle(input, input.Length));
            try
            {
                for (var requests = 0; requests < 8; requests++)
                {
                    var head = reader.ReadHeadAsync(CancellationToken.None).GetAwaiter().GetResult();
                    if (head is null)
                        return;
                    var body = reader.ReadBodyAsync(head, 256, CancellationToken.None).GetAwaiter().GetResult();
                    Assert.InRange(body.Length, 0, 256);
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
            {
            }
        });
    }

    [Fact]
    public void The_http_reader_copies_a_chunked_body_and_throws_only_its_own_errors()
    {
        Fuzz(42, HttpSeeds, input =>
        {
            var reader = new HttpReader(new Trickle(input, input.Length));
            try
            {
                var head = reader.ReadHeadAsync(CancellationToken.None).GetAwaiter().GetResult();
                if (head is { IsChunked: true })
                    reader.CopyChunkedAsync(Stream.Null, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
            {
            }
        });
    }

    [Theory]
    [InlineData("application/x-www-form-urlencoded", "login=me&password=cw%3A%2F%2FPW&code=cw://T&x=a+b%20c")]
    [InlineData("application/json", """{"login":"me","password":"cw://PW","nested":{"code":"cw://T"},"list":["cw://X",1,true,null]}""")]
    [InlineData("text/plain", "any cw://PW text")]
    public void The_body_scan_and_fill_never_throw(string contentType, string seed)
    {
        Fuzz(43, [seed], input =>
        {
            var scan = ProxyBody.Scan(input, contentType);
            var values = scan.Fields.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(name => name, _ => "v&a\"l=ue%", StringComparer.OrdinalIgnoreCase);
            var filled = ProxyBody.Fill(input, contentType, values);
            Assert.Empty(ProxyBody.Scan(filled, contentType).Fields);
        });
    }

    [Fact]
    public void The_dotenv_parser_never_throws()
    {
        Fuzz(44, ["# app\nexport API_KEY=sk-123 # note\nPORT=3000\nTOKEN=\"a b\"\nREF=cw://API_KEY\n\n'BAD\n"], input =>
        {
            foreach (var line in DotEnvFile.Parse(Encoding.UTF8.GetString(input)))
                _ = (line.VaultRef, line.Key is null || DotEnvFile.LooksSecret(line.Key));
        });
    }

    [Fact]
    public void The_hook_command_parser_never_throws()
    {
        Fuzz(45,
        [
            "cw policy set k gh Full && gh auth token | clip",
            "cmd /c \"powershell -c \\\"bash -c 'cw agent stop'\\\"\"",
            "Start-Process -FilePath cw -ArgumentList 'unharden gh'; cat .env",
            "& 'C:\\app\\cw.exe' read X; dotnet exec C:/x/cw.dll uninstall",
        ], input =>
        {
            var command = Encoding.UTF8.GetString(input);
            _ = PolicyHook.WeakeningCwCalls(command);
            _ = PolicyHook.RevealCwCalls(command);
            _ = PolicyHook.FindToolCalls(command);
            _ = PolicyHook.EnvFileReads(command);
        });
    }

    [Fact]
    public void The_agent_reason_is_one_short_line_of_plain_text()
    {
        Fuzz(46, ["create the release PR", "line one\nline two\u202e\u200b\ud83d\ude00"], input =>
        {
            if (AgentReason.Clean(Encoding.UTF8.GetString(input)) is { } clean)
            {
                Assert.InRange(clean.Length, 1, AgentReason.MaxLength);
                Assert.DoesNotContain(clean, c => char.IsControl(c) || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.Format);
            }
        });
    }

    [Fact]
    public void A_one_time_code_is_digits_or_a_format_error()
    {
        Fuzz(47, ["GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", "otpauth://totp/T:me?secret=GEZDGNBVGY3TQOJQ&digits=8&period=60&algorithm=SHA256"], input =>
        {
            try
            {
                Assert.Matches("^[0-9]{6,8}$", OneTimeCode.From(Encoding.UTF8.GetString(input), DateTimeOffset.UnixEpoch.AddDays(1)));
            }
            catch (FormatException)
            {
            }
        });
    }
}

/// <summary>#97: the faults that the fuzz tests found, and the request framing that a proxy must refuse.</summary>
public class FuzzFoundTests
{
    private static HttpRequestHead? Head(string text) =>
        new HttpReader(new MemoryStream(Encoding.Latin1.GetBytes(text))).ReadHeadAsync(CancellationToken.None).GetAwaiter().GetResult();

    [Fact]
    public void A_json_body_with_bad_utf8_in_a_string_is_loose_text()
    {
        byte[] body = [.. "{\"password\":\""u8, 0xC6, .. "cw://PW\"}"u8];
        var scan = ProxyBody.Scan(body, "application/json");
        Assert.Empty(scan.Fields);
    }

    [Fact]
    public void A_totp_link_that_names_a_setting_twice_is_a_format_error() =>
        Assert.Throws<FormatException>(() => OneTimeCode.From(
            "otpauth://totp/T?secret=GEZDGNBVGY3TQOJQ&period=30&period=60", DateTimeOffset.UnixEpoch));

    [Theory]
    [InlineData("7ffffffffffffffd")]
    [InlineData("80000000")]
    public async Task A_huge_chunk_is_too_long_before_the_body_is_read(string size)
    {
        var text = "POST / HTTP/1.1\r\nHost: a\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhello\r\n" + size + "\r\n" + new string('x', 4096);
        var reader = new HttpReader(new MemoryStream(Encoding.Latin1.GetBytes(text)));
        var head = (await reader.ReadHeadAsync(CancellationToken.None))!;
        var ex = await Assert.ThrowsAnyAsync<InvalidDataException>(() => reader.ReadBodyAsync(head, 256, CancellationToken.None));
        Assert.Contains("too long", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Content-Length: 5\r\nContent-Length: 6")]
    [InlineData("Content-Length: 5\r\nTransfer-Encoding: chunked")]
    [InlineData("Transfer-Encoding: xchunked")]
    [InlineData("Transfer-Encoding: chunked, gzip")]
    [InlineData("Transfer-Encoding: chunked\r\nTransfer-Encoding: chunked")]
    [InlineData("Content-Length: -5")]
    [InlineData("Content-Length: 5x")]
    public void A_request_whose_framing_can_be_read_two_ways_is_refused(string headers) =>
        Assert.Throws<InvalidDataException>(() => Head($"POST / HTTP/1.1\r\nHost: a\r\n{headers}\r\n\r\n"));

    [Theory]
    [InlineData("Content-Length: 5\r\ncontent-length: 5", false)]
    [InlineData("Transfer-Encoding: Chunked", true)]
    [InlineData("Transfer-Encoding: gzip, chunked", true)]
    public void A_request_with_one_clear_framing_is_read(string headers, bool chunked) =>
        Assert.Equal(chunked, Head($"POST / HTTP/1.1\r\nHost: a\r\n{headers}\r\n\r\n")!.IsChunked);
}
