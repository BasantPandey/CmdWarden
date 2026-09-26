using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace CmdWarden.Tests;

/// <summary>
/// The GitHub REST calls of the app token flow (#40), on 127.0.0.1. It checks the RS256 JWT
/// against the app key. The app is installed on every repo except owner/missing.
/// </summary>
internal sealed class FakeGitHubApi : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly RSA _publicKey;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private int _tokens;

    public string Url { get; }
    public int TokensCreated => Volatile.Read(ref _tokens);
    public string? LastTokenBody { get; private set; }

    public FakeGitHubApi(RSA appKey)
    {
        _publicKey = RSA.Create(appKey.ExportParameters(false));
        _listener.Start();
        Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
        _loop = Task.Run(LoopAsync);
    }

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using var _ = client;
        var stream = client.GetStream();
        var head = new StringBuilder();
        var one = new byte[1];
        while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal) && await stream.ReadAsync(one) == 1)
            head.Append((char)one[0]);
        var lines = head.ToString().Split("\r\n");
        var (method, path) = lines[0].Split(' ') is [var m, var p, ..] ? (m, p) : ("", "");
        var headers = lines.Skip(1).Where(l => l.Contains(':')).ToDictionary(l => l[..l.IndexOf(':')].Trim(), l => l[(l.IndexOf(':') + 1)..].Trim(), StringComparer.OrdinalIgnoreCase);
        var body = "";
        if (headers.TryGetValue("Content-Length", out var length) && int.Parse(length) is > 0 and var n)
        {
            var buffer = new byte[n];
            await stream.ReadExactlyAsync(buffer);
            body = Encoding.UTF8.GetString(buffer);
        }

        var (status, json) = Route(method, path, headers.GetValueOrDefault("Authorization"), body);
        var bytes = Encoding.UTF8.GetBytes(json);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} X\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n"));
        await stream.WriteAsync(bytes);
    }

    private (int Status, string Json) Route(string method, string path, string? authorization, string body)
    {
        if (authorization is null || !authorization.StartsWith("Bearer ", StringComparison.Ordinal) || !JwtValid(authorization[7..]))
            return (401, """{"message":"Bad credentials"}""");
        if (method == "GET" && path == "/app")
            return (200, """{"slug":"cmdwarden-test","html_url":"https://github.com/apps/cmdwarden-test"}""");
        if (method == "GET" && path.StartsWith("/repos/", StringComparison.Ordinal) && path.EndsWith("/installation", StringComparison.Ordinal))
            return path == "/repos/owner/missing/installation" ? (404, """{"message":"Not Found"}""") : (200, """{"id":42}""");
        if (method == "POST" && path == "/app/installations/42/access_tokens")
        {
            LastTokenBody = body;
            var n = Interlocked.Increment(ref _tokens);
            return (201, new JsonObject { ["token"] = $"ghs_fake_{n}", ["expires_at"] = DateTimeOffset.UtcNow.AddHours(1).ToString("o") }.ToJsonString());
        }
        return (404, """{"message":"Not Found"}""");
    }

    private bool JwtValid(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3)
            return false;
        static byte[] B64(string s) => Convert.FromBase64String(s.Replace('-', '+').Replace('_', '/').PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
        var payload = JsonNode.Parse(B64(parts[1]))!;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return _publicKey.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), B64(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            && payload["iss"]!.GetValue<string>() == "7"
            && payload["iat"]!.GetValue<long>() <= now && payload["exp"]!.GetValue<long>() > now;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try { await _loop; } catch (OperationCanceledException) { }
        _publicKey.Dispose();
        _stop.Dispose();
    }
}
