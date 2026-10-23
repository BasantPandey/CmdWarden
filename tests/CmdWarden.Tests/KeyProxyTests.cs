using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CmdWarden.Agent.Proxy;
using CmdWarden.Contracts.Proxy;

namespace CmdWarden.Tests;

public class KeyProxyTests
{
    [Fact]
    public async Task Reader_reads_heads_and_copies_bodies_without_reading_ahead()
    {
        var wire = "POST /v1/x HTTP/1.1\r\nHost: api\r\nContent-Length: 5\r\nAuthorization: Bearer cw://KEY\r\n\r\nhello"
                   + "POST /c HTTP/1.1\r\nTransfer-Encoding: chunked\r\n\r\n3\r\nabc\r\n0\r\nX-T: 1\r\n\r\n"
                   + "GET /ws HTTP/1.1\r\nUpgrade: websocket\r\n\r\nRAW";
        var reader = new HttpReader(new MemoryStream(Encoding.Latin1.GetBytes(wire)));

        var first = (await reader.ReadHeadAsync(default))!;
        Assert.Equal(("POST", "/v1/x"), (first.Method, first.Target));
        Assert.Equal(5, first.ContentLength);
        Assert.Equal(["KEY"], KeyProxy.Placeholders(first.Texts));
        var body = new MemoryStream();
        await reader.CopyAsync(body, 5, default);
        Assert.Equal("hello", Encoding.Latin1.GetString(body.ToArray()));

        var second = (await reader.ReadHeadAsync(default))!;
        Assert.True(second.IsChunked);
        var chunked = new MemoryStream();
        await reader.CopyChunkedAsync(chunked, default);
        Assert.Equal("3\r\nabc\r\n0\r\nX-T: 1\r\n\r\n", Encoding.Latin1.GetString(chunked.ToArray()));

        var third = (await reader.ReadHeadAsync(default))!;
        Assert.True(third.IsUpgrade);
        var rest = new MemoryStream();
        await reader.CopyRestAsync(rest, default);
        Assert.Equal("RAW", Encoding.Latin1.GetString(rest.ToArray()));
        Assert.Null(await reader.ReadHeadAsync(default));
    }

    [Fact]
    public async Task Reader_refuses_a_bad_or_huge_head()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new HttpReader(new MemoryStream("NOT HTTP\r\n\r\n"u8.ToArray())).ReadHeadAsync(default));
        var huge = Encoding.Latin1.GetBytes("GET / HTTP/1.1\r\nX: " + new string('a', HttpReader.MaxHead));
        await Assert.ThrowsAsync<InvalidDataException>(() => new HttpReader(new MemoryStream(huge)).ReadHeadAsync(default));
    }

    [Fact]
    public void Rewrite_puts_values_in_the_target_and_headers_and_keeps_order()
    {
        var head = new HttpRequestHead { Method = "GET", Target = "/v1?key=cw://G" };
        head.Headers.Add(new("x-api-key", "cw://A"));
        head.Headers.Add(new("Host", "h"));
        head.Rewrite(t => KeyProxy.Placeholder().Replace(t, m => m.Groups[1].Value + "-value"));
        Assert.Equal("GET /v1?key=G-value HTTP/1.1\r\nx-api-key: A-value\r\nHost: h\r\n\r\n", Encoding.Latin1.GetString(head.ToBytes()));
    }

    [Theory]
    [InlineData("api.openai.com", "api.openai.com", true)]
    [InlineData("API.OpenAI.com", "api.openai.com.", true)]
    [InlineData("*.example.com", "a.b.example.com", true)]
    [InlineData("*.example.com", "example.com", false)]
    [InlineData("api.openai.com", "api.openai.com.evil.io", false)]
    public void Hosts_match_exactly_or_by_a_star_prefix(string pattern, string host, bool expected) =>
        Assert.Equal(expected, KeyProxy.HostMatches(pattern, host));

    [Fact]
    public void Config_allows_a_key_only_on_its_hosts()
    {
        var config = new KeyProxyConfig();
        config.Keys["OPENAI_API_KEY"] = new ProxyKey { Hosts = ["api.openai.com"] };
        Assert.True(config.Allows("openai_api_key", "api.openai.com"));
        Assert.False(config.Allows("OPENAI_API_KEY", "evil.io"));
        Assert.False(config.Allows("OTHER", "api.openai.com"));
        Assert.True(config.Lists("api.openai.com"));
        Assert.False(config.Lists("github.com"));
    }

    [Fact]
    public void A_sign_in_key_goes_only_in_its_field_and_path()
    {
        var config = new KeyProxyConfig();
        config.Keys["WEB_PASSWORD"] = new ProxyKey { Hosts = ["github.com"], Bodies = [new ProxyBodyField { Path = "/session", Field = "password" }] };
        Assert.True(config.AllowsBody("WEB_PASSWORD", "github.com", "/session", "password"));
        Assert.False(config.AllowsBody("WEB_PASSWORD", "github.com", "/session", "login"));
        Assert.False(config.AllowsBody("WEB_PASSWORD", "github.com", "/gist", "password"));
        Assert.False(config.AllowsBody("WEB_PASSWORD", "evil.io", "/session", "password"));
        Assert.False(config.Allows("WEB_PASSWORD", "github.com"));
        Assert.True(config.HasBodyPath("github.com", "/session"));
        Assert.False(config.HasBodyPath("github.com", "/login"));
    }

    [Fact]
    public void A_form_body_gets_the_value_only_in_a_whole_field_value()
    {
        const string form = "application/x-www-form-urlencoded";
        var body = "login=me%40x.io&password=cw%3A%2F%2FWEB_PASSWORD&next=%2F"u8.ToArray();
        var scan = ProxyBody.Scan(body, form);
        Assert.Equal([new BodyPlaceholder("password", "WEB_PASSWORD")], scan.Fields);
        Assert.Empty(scan.Loose);
        var filled = ProxyBody.Fill(body, form, new Dictionary<string, string> { ["WEB_PASSWORD"] = "p& w=ü" });
        Assert.Equal("login=me%40x.io&password=p%26%20w%3D%C3%BC&next=%2F", Encoding.ASCII.GetString(filled));

        var partial = ProxyBody.Scan("login=hi+cw%3A%2F%2FWEB_PASSWORD"u8.ToArray(), form + "; charset=UTF-8");
        Assert.Empty(partial.Fields);
        Assert.Equal(["WEB_PASSWORD"], partial.Loose);
    }

    [Fact]
    public void A_json_body_gets_the_value_only_in_a_property_value()
    {
        var body = """{"user":{"name":"me","password":"cw://WEB_PASSWORD"},"tags":["cw://WEB_PASSWORD"]}"""u8.ToArray();
        var scan = ProxyBody.Scan(body, "application/json");
        Assert.Equal([new BodyPlaceholder("password", "WEB_PASSWORD")], scan.Fields);
        Assert.Equal(["WEB_PASSWORD"], scan.Loose);

        var one = """{ "password" : "cw:\/\/WEB_PASSWORD", "n": 1 }"""u8.ToArray();
        var filled = ProxyBody.Fill(one, "application/json", new Dictionary<string, string> { ["WEB_PASSWORD"] = "a\"b" });
        Assert.Equal("{ \"password\" : \"a\\u0022b\", \"n\": 1 }", Encoding.UTF8.GetString(filled));
        Assert.Equal("a\"b", JsonDocument.Parse(filled).RootElement.GetProperty("password").GetString());

        Assert.Equal(["X"], ProxyBody.Scan("""{"password":"cw://X" """u8.ToArray(), "application/json").Loose);
        Assert.Equal(["X"], ProxyBody.Scan("--b\r\n\r\ncw://X\r\n--b--"u8.ToArray(), "multipart/form-data; boundary=b").Loose);
    }

    [Fact]
    public async Task Reader_reads_a_chunked_body_whole_and_refuses_a_big_one()
    {
        var reader = new HttpReader(new MemoryStream("3\r\nabc\r\n2\r\nde\r\n0\r\nX-T: 1\r\n\r\nNEXT"u8.ToArray()));
        var head = new HttpRequestHead { Method = "POST", Target = "/" };
        head.Headers.Add(new("Transfer-Encoding", "chunked"));
        head.Headers.Add(new("Expect", "100-continue"));
        Assert.Equal("abcde", Encoding.ASCII.GetString(await reader.ReadBodyAsync(head, 10, default)));
        head.UseLength(5);
        Assert.Equal("POST / HTTP/1.1\r\nContent-Length: 5\r\n\r\n", Encoding.Latin1.GetString(head.ToBytes()));

        var big = new HttpReader(new MemoryStream("5\r\nabcde\r\n0\r\n\r\n"u8.ToArray()));
        var chunked = new HttpRequestHead { Method = "POST", Target = "/" };
        chunked.Headers.Add(new("Transfer-Encoding", "chunked"));
        await Assert.ThrowsAsync<InvalidDataException>(() => big.ReadBodyAsync(chunked, 4, default));
    }

    [Fact]
    public async Task Tcp_owner_is_the_process_that_connects()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            using var accepted = await listener.AcceptTcpClientAsync();
            Assert.Equal(Environment.ProcessId,
                TcpOwner.Find((IPEndPoint)accepted.Client.RemoteEndPoint!, (IPEndPoint)accepted.Client.LocalEndPoint!));
        }
        finally
        {
            listener.Stop();
        }
    }
}
