using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using CmdWarden.Agent.Identity;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Proxy;

namespace CmdWarden.Agent.Proxy;

/// <summary>
/// The placeholder proxy (#41) on 127.0.0.1. A CONNECT to a host that a key lists ends TLS here
/// with a certificate from the per-user CA; each request with cw://NAME goes through the launcher
/// policy and the Approval Gate, then gets the vault value. Other hosts pass through untouched, or
/// get 403 in strict mode. A placeholder for a host its key does not list gets 403. A GET of the
/// CRL path gets the empty CRL of the CA.
/// #85: for a path that a key names with a body field, the proxy reads the body too, and only that
/// field gets the value. Such a swap always shows the Approval Gate.
/// ponytail: HTTP/1.1 only, and no pipelined requests; clients send the next request after the
/// response ends. Other bodies pass as they are.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class KeyProxyServer(SessionAgentService gate, CredentialVault vault, AgentRuntimeInfo runtime, ILogger<KeyProxyServer> log)
    : BackgroundService
{
    /// <summary>Tests only: a PEM file of one more CA that upstream TLS may chain to.</summary>
    public const string UpstreamCaEnv = "CW_PROXY_UPSTREAM_CA";

    private readonly X509Certificate2? _extraUpstreamCa = Environment.GetEnvironmentVariable(UpstreamCaEnv) is { Length: > 0 } pem
        ? X509Certificate2.CreateFromPem(File.ReadAllText(pem))
        : null;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = KeyProxy.Load(runtime.ProductRoot);
        using var ca = ProxyCa.Open(config?.CaThumbprint, config?.Port);
        if (config is null || ca is null)
        {
            log.LogWarning("proxy off: no CA in the certificate store; run cw proxy setup");
            return;
        }
        var listener = new TcpListener(IPAddress.Loopback, config.Port);
        try
        {
            listener.Start();
        }
        catch (SocketException ex)
        {
            log.LogError("proxy off: port {Port}: {Message}", config.Port, ex.Message);
            return;
        }
        using var registration = stoppingToken.Register(listener.Stop);
        while (!stoppingToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }
            _ = Task.Run(() => ServeAsync(client, ca, stoppingToken), stoppingToken);
        }
    }

    private async Task ServeAsync(TcpClient client, ProxyCa ca, CancellationToken ct)
    {
        using var _ = client;
        var pid = TcpOwner.Find((IPEndPoint)client.Client.RemoteEndPoint!, (IPEndPoint)client.Client.LocalEndPoint!);
        // Only processes of this user: another user on the PC must not use the vault of this one.
        if (pid is not { } clientPid || !PipeCaller.IsOwnerProcess(clientPid))
            return;
        var stream = client.GetStream();
        try
        {
            var reader = new HttpReader(stream);
            if (await reader.ReadHeadAsync(ct).ConfigureAwait(false) is not { } head)
                return;
            // The config can change while the agent runs; read it for each connection.
            var config = KeyProxy.Load(runtime.ProductRoot) ?? new KeyProxyConfig();
            if (head.Target == ProxyCa.CrlPath(ca.Certificate.Thumbprint))
                await ReplyAsync(stream, 200, "application/pkix-crl", ca.Crl(), ct).ConfigureAwait(false);
            else if (head.Method.Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
                await ConnectAsync(clientPid, head, reader, stream, config, ca, ct).ConfigureAwait(false);
            else
                await PlainAsync(clientPid, head, reader, stream, config, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidDataException or AuthenticationException
                                   or OperationCanceledException or ObjectDisposedException)
        {
            log.LogDebug("proxy: connection ends: {Message}", ex.Message);
        }
    }

    private async Task ConnectAsync(int pid, HttpRequestHead head, HttpReader reader, NetworkStream client,
        KeyProxyConfig config, ProxyCa ca, CancellationToken ct)
    {
        var (host, port) = SplitHostPort(head.Target, 443);
        if (!config.Lists(host))
        {
            if (config.Strict)
            {
                await ReplyAsync(client, 403, $"{ProductInfo.Name}: {host} is not on the proxy list.", ct).ConfigureAwait(false);
                return;
            }
            using var tunnel = new TcpClient();
            await tunnel.ConnectAsync(host, port, ct).ConfigureAwait(false);
            await client.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(), ct).ConfigureAwait(false);
            var upstream = tunnel.GetStream();
            await Task.WhenAny(reader.CopyRestAsync(upstream, ct), upstream.CopyToAsync(client, ct)).ConfigureAwait(false);
            return;
        }

        await client.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(), ct).ConfigureAwait(false);
        await using var tls = new SslStream(new PrefixStream(reader, client));
        await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = ca.LeafFor(host),
            ApplicationProtocols = [SslApplicationProtocol.Http11],
        }, ct).ConfigureAwait(false);

        using var remote = new TcpClient();
        await remote.ConnectAsync(host, port, ct).ConfigureAwait(false);
        await using var upstreamTls = new SslStream(remote.GetStream(), false, ValidateUpstream);
        await upstreamTls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = host,
            ApplicationProtocols = [SslApplicationProtocol.Http11],
        }, ct).ConfigureAwait(false);
        await RelayAsync(pid, host, "https", new HttpReader(tls), tls, upstreamTls, config, ct).ConfigureAwait(false);
    }

    private async Task PlainAsync(int pid, HttpRequestHead first, HttpReader reader, NetworkStream client,
        KeyProxyConfig config, CancellationToken ct)
    {
        if (!Uri.TryCreate(first.Target, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp)
        {
            await ReplyAsync(client, 400, $"{ProductInfo.Name}: the proxy takes CONNECT or an http:// URL.", ct).ConfigureAwait(false);
            return;
        }
        if (config.Strict && !config.Lists(uri.Host))
        {
            await ReplyAsync(client, 403, $"{ProductInfo.Name}: {uri.Host} is not on the proxy list.", ct).ConfigureAwait(false);
            return;
        }
        using var remote = new TcpClient();
        await remote.ConnectAsync(uri.Host, uri.Port, ct).ConfigureAwait(false);
        await RelayAsync(pid, uri.Host, "http", reader, client, remote.GetStream(), config, ct, first).ConfigureAwait(false);
    }

    /// <summary>
    /// Requests go one by one from the client to the upstream, each checked for placeholders.
    /// Responses go back as raw bytes. A refused request gets 403, and the connection closes. When
    /// the upstream closes, the connection closes too, so the client does not wait for an answer.
    /// </summary>
    private async Task RelayAsync(int pid, string host, string scheme, HttpReader reader, Stream client, Stream upstream,
        KeyProxyConfig config, CancellationToken ct, HttpRequestHead? first = null)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var responses = upstream.CopyToAsync(client, stop.Token);
        var requests = RequestsAsync(pid, host, scheme, reader, client, upstream, config, stop, ct, first);
        try
        {
            await Task.WhenAny(requests, responses).ConfigureAwait(false);
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            try { await responses.ConfigureAwait(false); } catch (Exception) { /* The connection ends. */ }
        }
        await requests.ConfigureAwait(false);
    }

    private async Task RequestsAsync(int pid, string host, string scheme, HttpReader reader, Stream client, Stream upstream,
        KeyProxyConfig config, CancellationTokenSource stop, CancellationToken ct, HttpRequestHead? first)
    {
        var token = stop.Token;
        var head = first;
        while (true)
        {
            head ??= await reader.ReadHeadAsync(token).ConfigureAwait(false);
            if (head is null)
                return;
            if (Uri.TryCreate(head.Target, UriKind.Absolute, out var absolute))
                head.Target = absolute.PathAndQuery;
            byte[]? body = null;
            if (config.HasBodyPath(host, PathOf(head)) && (head.IsChunked || head.ContentLength > 0))
            {
                if (head.ContentLength > ProxyBody.MaxLength)
                {
                    await stop.CancelAsync().ConfigureAwait(false);
                    await ReplyAsync(client, 413, $"{ProductInfo.Name}: a sign-in body is at most {ProxyBody.MaxLength} bytes.", ct).ConfigureAwait(false);
                    return;
                }
                body = await reader.ReadBodyAsync(head, ProxyBody.MaxLength, token).ConfigureAwait(false);
            }
            var (refusal, filled) = Check(pid, host, scheme, head, body, config);
            if (refusal is not null)
            {
                await stop.CancelAsync().ConfigureAwait(false);
                await ReplyAsync(client, 403, refusal, ct).ConfigureAwait(false);
                return;
            }
            if ((filled ?? body) is { } send)
            {
                head.UseLength(send.Length);
                await upstream.WriteAsync(head.ToBytes(), token).ConfigureAwait(false);
                await upstream.WriteAsync(send, token).ConfigureAwait(false);
            }
            else
            {
                await upstream.WriteAsync(head.ToBytes(), token).ConfigureAwait(false);
                if (head.IsChunked)
                    await reader.CopyChunkedAsync(upstream, token).ConfigureAwait(false);
                else if (head.ContentLength is > 0 and var length)
                    await reader.CopyAsync(upstream, length, token).ConfigureAwait(false);
            }
            await upstream.FlushAsync(token).ConfigureAwait(false);
            if (head.IsUpgrade)
            {
                // A WebSocket: after the upgrade the bytes are no longer HTTP.
                await reader.CopyRestAsync(upstream, token).ConfigureAwait(false);
                return;
            }
            head = null;
        }
    }

    /// <summary>
    /// The 403 text, or null when the request may go. Puts the vault values in place in the head,
    /// and gives back the filled body when the body has field placeholders.
    /// </summary>
    private (string? Refusal, byte[]? Body) Check(int pid, string host, string scheme, HttpRequestHead head, byte[]? body,
        KeyProxyConfig config)
    {
        var path = PathOf(head);
        var contentType = head.Header("Content-Type");
        var scan = body is null ? new BodyScan([], []) : ProxyBody.Scan(body, contentType);
        if (scan.Loose.Count > 0)
            return ($"{ProductInfo.Name}: cw://{scan.Loose[0]} must be the whole value of a field that cw proxy add --field names.", null);
        foreach (var field in scan.Fields)
        {
            if (!config.AllowsBody(field.Name, host, path, field.Field))
                return ($"{ProductInfo.Name}: cw://{field.Name} is not allowed in the field {field.Field} of {host}{path}.", null);
        }

        var headNames = KeyProxy.Placeholders(head.Texts);
        var names = headNames.Concat(scan.Fields.Select(f => f.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count == 0)
            return (null, null);
        foreach (var name in headNames)
        {
            if (!config.Keys.TryGetValue(name, out var key))
                return ($"{ProductInfo.Name}: cw://{name} is not a proxy key. Add it: cw proxy add {name} --host {host}", null);
            if (key.Bodies.Count > 0)
                return ($"{ProductInfo.Name}: cw://{name} goes only in a sign-in field, not in a header.", null);
            if (!config.Allows(name, host))
                return ($"{ProductInfo.Name}: cw://{name} is not allowed for {host}.", null);
        }
        var fields = scan.Fields.Count == 0 ? null : string.Join(", ", scan.Fields.Select(f => f.Field).Distinct(StringComparer.Ordinal));
        if (!gate.AuthorizeProxyUse(pid, names, head.Method, $"{scheme}://{host}{head.Target}", fields))
            return ($"{ProductInfo.Name}: the use of {string.Join(", ", names)} was denied.", null);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            try
            {
                var bytes = vault.Read(name);
                values[name] = Encoding.UTF8.GetString(bytes);
                Array.Clear(bytes);
            }
            catch (KeyNotFoundException)
            {
                return ($"{ProductInfo.Name}: {name} is not in the vault. Save it: cw save {name}", null);
            }
        }
        head.Rewrite(text => KeyProxy.Placeholder().Replace(text, m => values[m.Groups[1].Value]));
        return (null, fields is null ? null : ProxyBody.Fill(body!, contentType, values));
    }

    private static string PathOf(HttpRequestHead head) => head.Target.Split('?')[0];

    private bool ValidateUpstream(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None)
            return true;
        if (_extraUpstreamCa is null || certificate is null || errors != SslPolicyErrors.RemoteCertificateChainErrors)
            return false;
        using var custom = new X509Chain();
        custom.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        custom.ChainPolicy.CustomTrustStore.Add(_extraUpstreamCa);
        custom.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return custom.Build(X509CertificateLoader.LoadCertificate(certificate.GetRawCertData()));
    }

    private static (string Host, int Port) SplitHostPort(string target, int defaultPort)
    {
        var colon = target.LastIndexOf(':');
        return colon > 0 && !target.EndsWith(']') && int.TryParse(target[(colon + 1)..], out var port)
            ? (target[..colon].Trim('[', ']'), port)
            : (target.Trim('[', ']'), defaultPort);
    }

    private static Task ReplyAsync(Stream client, int status, string text, CancellationToken ct) =>
        ReplyAsync(client, status, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text + "\n"), ct);

    private static async Task ReplyAsync(Stream client, int status, string contentType, byte[] body, CancellationToken ct)
    {
        var reason = status switch { 200 => "OK", 403 => "Forbidden", 400 => "Bad Request", 413 => "Content Too Large", _ => "Error" };
        var head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        await client.WriteAsync(Encoding.ASCII.GetBytes(head), ct).ConfigureAwait(false);
        await client.WriteAsync(body, ct).ConfigureAwait(false);
        await client.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The TLS bytes that the reader took after CONNECT, then the socket.</summary>
    private sealed class PrefixStream(HttpReader reader, Stream socket) : Stream
    {
        private MemoryStream? _prefix;

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_prefix is null)
            {
                _prefix = new MemoryStream();
                await reader.CopyBufferedAsync(_prefix, ct).ConfigureAwait(false);
                _prefix.Position = 0;
            }
            if (_prefix.Position < _prefix.Length)
                return await _prefix.ReadAsync(buffer, ct).ConfigureAwait(false);
            return await socket.ReadAsync(buffer, ct).ConfigureAwait(false);
        }

        public override void Write(byte[] buffer, int offset, int count) => socket.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => socket.WriteAsync(buffer, ct);
        public override Task FlushAsync(CancellationToken ct) => socket.FlushAsync(ct);
        public override void Flush() => socket.Flush();
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
