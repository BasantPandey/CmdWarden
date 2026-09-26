using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using CmdWarden.Agent.Proxy;
using CmdWarden.Cli;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Proxy;

namespace CmdWarden.Tests;

/// <summary>
/// #41: a client with cw://NAME reaches the API through the proxy of a real Session Agent, and the
/// API gets the vault value. A key on a host it does not list, a host off the list in strict mode,
/// and a write that needs the popup (approval off in tests) all get 403.
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class KeyProxyProcessTests
{
    [Fact]
    public async Task The_proxy_puts_the_key_in_place_for_listed_hosts_only()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = Path.Combine(Path.GetTempPath(), "cw-prod-" + Guid.NewGuid().ToString("N"));
        var port = FreePort();
        using var caCert = ProxyCa.Create();
        using var ca = ProxyCa.Open(caCert.Thumbprint, port)!;
        try
        {
            var config = new KeyProxyConfig { Port = port, CaThumbprint = caCert.Thumbprint };
            config.Keys["TEST_API_KEY"] = new ProxyKey { Hosts = ["localhost"] };
            config.Keys["OTHER_KEY"] = new ProxyKey { Hosts = ["api.other.test"] };
            KeyProxy.Save(config, root);
            var caPem = Path.Combine(root, "test-ca.pem");
            await File.WriteAllTextAsync(caPem, caCert.ExportCertificatePem());

            await using var api = new EchoApi(ca.LeafFor("localhost"));
            await using var agent = await TestAgent.StartAsync(productRoot: root,
                env: new Dictionary<string, string> { [KeyProxyServer.UpstreamCaEnv] = caPem });
            if (agent is null)
                return;
            agent.Enroll(LauncherEnrollmentKind.Terminal);
            var secret = "sk-test-" + Guid.NewGuid().ToString("N");
            await AgentVaultClient.SaveAsync("TEST_API_KEY", Encoding.UTF8.GetBytes(secret), agent.PipeName);
            await WaitForPortAsync(port);

            using var http = Client(port, caCert);
            async Task<(HttpStatusCode Status, string Body)> Send(HttpMethod method, string url, string auth, string? body = null)
            {
                using var request = new HttpRequestMessage(method, url);
                request.Headers.TryAddWithoutValidation("Authorization", auth);
                if (body is not null)
                    request.Content = new StringContent(body);
                using var response = await http.SendAsync(request);
                return (response.StatusCode, await response.Content.ReadAsStringAsync());
            }

            var api1 = $"https://localhost:{api.Port}/v1/echo";
            var get = await Send(HttpMethod.Get, api1, "Bearer cw://TEST_API_KEY");
            Assert.Equal(HttpStatusCode.OK, get.Status);
            Assert.Equal($"Bearer {secret}", JsonNode.Parse(get.Body)!["authorization"]!.GetValue<string>());

            // Terminal is Trusted: a POST with a body is a write and runs with no popup.
            var post = await Send(HttpMethod.Post, api1, "Bearer cw://TEST_API_KEY", new string('x', 70000));
            Assert.Equal(HttpStatusCode.OK, post.Status);
            Assert.Equal(70000, JsonNode.Parse(post.Body)!["length"]!.GetValue<int>());

            var wrongHost = await Send(HttpMethod.Get, api1, "Bearer cw://OTHER_KEY");
            Assert.Equal(HttpStatusCode.Forbidden, wrongHost.Status);
            Assert.Contains("not allowed for localhost", wrongHost.Body, StringComparison.Ordinal);

            // Off the list: passes through untouched; in strict mode, 403.
            var offList = $"https://127.0.0.1:{api.Port}/v1/echo";
            var passed = await Send(HttpMethod.Get, offList, "Bearer cw://TEST_API_KEY");
            Assert.Equal(HttpStatusCode.OK, passed.Status);
            Assert.Equal("Bearer cw://TEST_API_KEY", JsonNode.Parse(passed.Body)!["authorization"]!.GetValue<string>());
            config.Strict = true;
            KeyProxy.Save(config, root);
            await Assert.ThrowsAsync<HttpRequestException>(() => Send(HttpMethod.Get, offList, "none"));

            // Read level for proxy: a write needs the popup, and approval is off.
            var store = new PolicyStore(agent.PolicyPath);
            store.Load();
            store.SetLevel(agent.SelectedPolicyKey, KeyProxy.Tool, PolicyLevel.Read);
            store.Save();
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, api1, "Bearer cw://TEST_API_KEY", "{}")).Status);
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, api1, "Bearer cw://TEST_API_KEY")).Status);

            var audit = string.Join("\n", Directory.GetFiles(Path.Combine(root, "audit"), "gates-*.ndjson").Select(File.ReadAllText));
            Assert.Contains("\"tool\":\"proxy\"", audit, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, audit, StringComparison.Ordinal);
        }
        finally
        {
            ProxyCa.Remove(caCert.Thumbprint);
            try { Directory.Delete(root, recursive: true); } catch { /* the agent may still hold a file */ }
        }
    }

    private static HttpClient Client(int port, X509Certificate2 ca) => new(new SocketsHttpHandler
    {
        Proxy = new WebProxy($"http://127.0.0.1:{port}"),
        UseProxy = true,
        PooledConnectionLifetime = TimeSpan.Zero,
        SslOptions = new SslClientAuthenticationOptions
        {
            // The test trusts the CA itself, as NODE_EXTRA_CA_CERTS does for Node. It checks
            // revocation online, as Schannel does for Windows curl: the proxy serves the CRL.
            RemoteCertificateValidationCallback = (_, cert, _, _) =>
            {
                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(ca);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
                return cert is not null && chain.Build(X509CertificateLoader.LoadCertificate(cert.GetRawCertData()));
            },
        },
    });

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static async Task WaitForPortAsync(int port)
    {
        for (var i = 0; i < 100; i++)
        {
            try
            {
                using var probe = new TcpClient();
                await probe.ConnectAsync(IPAddress.Loopback, port);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(100);
            }
        }
        Assert.Fail($"The proxy did not listen on {port}.");
    }

    /// <summary>An HTTPS API on 127.0.0.1 that answers with the Authorization header and the body length.</summary>
    private sealed class EchoApi : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly X509Certificate2 _cert;
        private readonly Task _loop;

        public int Port { get; }

        public EchoApi(X509Certificate2 cert)
        {
            _cert = cert;
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _loop = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    TcpClient client;
                    try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
                    catch (OperationCanceledException) { return; }
                    _ = Task.Run(() => ServeAsync(client));
                }
            });
        }

        private async Task ServeAsync(TcpClient client)
        {
            using var _ = client;
            try
            {
                await using var tls = new SslStream(client.GetStream());
                await tls.AuthenticateAsServerAsync(_cert);
                var reader = new HttpReader(tls);
                while (await reader.ReadHeadAsync(_stop.Token) is { } head)
                {
                    var body = new MemoryStream();
                    if (head.IsChunked)
                        await reader.CopyChunkedAsync(body, _stop.Token);
                    else if (head.ContentLength is > 0 and var n)
                        await reader.CopyAsync(body, n, _stop.Token);
                    var json = new JsonObject { ["authorization"] = head.Header("Authorization"), ["length"] = (int)body.Length }.ToJsonString();
                    var bytes = Encoding.UTF8.GetBytes(json);
                    await tls.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\n\r\n"));
                    await tls.WriteAsync(bytes);
                    await tls.FlushAsync();
                }
            }
            catch (Exception ex) when (ex is IOException or System.Security.Authentication.AuthenticationException or OperationCanceledException or InvalidDataException)
            {
                // The client left.
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            try { await _loop; } catch (OperationCanceledException) { }
        }
    }
}
