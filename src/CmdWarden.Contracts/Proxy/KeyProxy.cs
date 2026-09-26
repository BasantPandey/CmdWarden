using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CmdWarden.Contracts.Proxy;

/// <summary>One vault entry that the proxy puts in place of cw://NAME, and the hosts that may get it.</summary>
public sealed class ProxyKey
{
    public List<string> Hosts { get; set; } = [];
}

/// <summary>The placeholder proxy (#41). The file is &lt;product root&gt;/proxy/config.json.</summary>
public sealed class KeyProxyConfig
{
    public int Port { get; set; } = KeyProxy.DefaultPort;
    /// <summary>True: a host that no key lists gets 403. False: its traffic passes through untouched.</summary>
    public bool Strict { get; set; }
    /// <summary>The CA certificate in the CurrentUser\My store.</summary>
    public string? CaThumbprint { get; set; }
    public Dictionary<string, ProxyKey> Keys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when a key lists the host. "*.example.com" matches every subdomain.</summary>
    public bool Lists(string host) => Keys.Values.Any(k => k.Hosts.Any(h => KeyProxy.HostMatches(h, host)));

    public bool Allows(string name, string host) =>
        Keys.TryGetValue(name, out var key) && key.Hosts.Any(h => KeyProxy.HostMatches(h, host));
}

/// <summary>
/// The placeholder proxy (#41): SDKs and curl send cw://NAME in place of an API key, through
/// HTTPS_PROXY. The Session Agent runs the proxy on 127.0.0.1. For a host that a key lists, it
/// ends TLS with a per-user CA, asks the launcher policy, and puts the vault value in place of the
/// placeholder. The app never sees the key.
/// </summary>
public static partial class KeyProxy
{
    public const string Tool = "proxy";
    public const int DefaultPort = 47831;
    public const string CaSubject = "CN=CmdWarden Proxy CA";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [GeneratedRegex(@"cw://([A-Za-z0-9_.\-]+)")]
    public static partial Regex Placeholder();

    public static string Dir(string? productRoot = null) => Path.Combine(productRoot ?? ProductPaths.Root(), "proxy");
    public static string ConfigPath(string? productRoot = null) => Path.Combine(Dir(productRoot), "config.json");
    /// <summary>The CA certificate (no key): NODE_EXTRA_CA_CERTS.</summary>
    public static string CaPemPath(string? productRoot = null) => Path.Combine(Dir(productRoot), "ca.pem");
    /// <summary>The Windows root certificates and the CA: SSL_CERT_FILE, REQUESTS_CA_BUNDLE, CURL_CA_BUNDLE.</summary>
    public static string BundlePath(string? productRoot = null) => Path.Combine(Dir(productRoot), "ca-bundle.pem");

    public static KeyProxyConfig? Load(string? productRoot = null)
    {
        var path = ConfigPath(productRoot);
        return File.Exists(path) ? JsonSerializer.Deserialize<KeyProxyConfig>(File.ReadAllText(path), JsonOptions) : null;
    }

    public static void Save(KeyProxyConfig config, string? productRoot = null)
    {
        Directory.CreateDirectory(Dir(productRoot));
        File.WriteAllText(ConfigPath(productRoot), JsonSerializer.Serialize(config, JsonOptions));
    }

    public static bool HostMatches(string pattern, string host)
    {
        pattern = pattern.Trim().ToLowerInvariant();
        host = host.Trim().TrimEnd('.').ToLowerInvariant();
        return pattern.StartsWith("*.", StringComparison.Ordinal)
            ? host.EndsWith(pattern[1..], StringComparison.Ordinal)
            : host == pattern;
    }

    /// <summary>The distinct placeholder names in the texts, in order.</summary>
    public static IReadOnlyList<string> Placeholders(IEnumerable<string> texts) =>
        texts.SelectMany(t => Placeholder().Matches(t).Select(m => m.Groups[1].Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// The env that sends a harness through the proxy and trusts its CA: HTTPS_PROXY for most tools,
    /// NODE_EXTRA_CA_CERTS for Node, and a CA bundle for Python, OpenSSL and curl.
    /// </summary>
    public static IReadOnlyDictionary<string, string> LaunchEnv(KeyProxyConfig config, string? productRoot = null)
    {
        var url = $"http://127.0.0.1:{config.Port}";
        var bundle = BundlePath(productRoot);
        return new Dictionary<string, string>
        {
            ["HTTPS_PROXY"] = url,
            ["HTTP_PROXY"] = url,
            ["NO_PROXY"] = "localhost,127.0.0.1,::1",
            ["NODE_USE_ENV_PROXY"] = "1",
            ["NODE_EXTRA_CA_CERTS"] = CaPemPath(productRoot),
            ["SSL_CERT_FILE"] = bundle,
            ["REQUESTS_CA_BUNDLE"] = bundle,
            ["CURL_CA_BUNDLE"] = bundle,
        };
    }

    /// <summary>
    /// Write ca.pem and ca-bundle.pem. The bundle holds the root certificates of Windows (user and
    /// machine) and the CA, so a client that uses it still trusts the hosts the proxy passes through.
    /// ponytail: Windows loads some roots on first use, so a rare root can be missing from the bundle.
    /// </summary>
    public static void WriteCaFiles(X509Certificate2 ca, string? productRoot = null)
    {
        Directory.CreateDirectory(Dir(productRoot));
        var caPem = ca.ExportCertificatePem();
        File.WriteAllText(CaPemPath(productRoot), caPem + "\n");
        var bundle = new StringBuilder();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, location) in new[] { (StoreName.Root, StoreLocation.CurrentUser), (StoreName.Root, StoreLocation.LocalMachine), (StoreName.AuthRoot, StoreLocation.LocalMachine) })
        {
            using var store = new X509Store(name, location);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            foreach (var cert in store.Certificates)
            {
                if (seen.Add(cert.Thumbprint))
                    bundle.AppendLine(cert.ExportCertificatePem());
                cert.Dispose();
            }
        }
        if (seen.Add(ca.Thumbprint))
            bundle.AppendLine(caPem);
        File.WriteAllText(BundlePath(productRoot), bundle.ToString());
    }
}
