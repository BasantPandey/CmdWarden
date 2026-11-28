using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace CmdWarden.Contracts;

/// <summary>STS refused the session, or the reply had no key. The long-term key must not go to the child.</summary>
public sealed class AwsSessionException : Exception
{
    public AwsSessionException()
        : base("AWS session mint failed.")
    {
    }
}

/// <summary>
/// Mint a 15-minute STS session for one aws run. The long-term key stays in the vault.
/// <c>sts get-session-token</c> and <c>sts get-federation-token</c> keep the long-term key.
/// </summary>
public static class AwsSession
{
    public const int DurationSeconds = 900;
    public const string Host = "sts.amazonaws.com";
    public const string Region = "us-east-1";
    public const string Service = "sts";
    public const string ContentType = "application/x-www-form-urlencoded; charset=utf-8";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Replace the long-term key in <paramref name="env"/> with a session key. A missing key does nothing.</summary>
    public static void Apply(
        IDictionary<string, string> env,
        IReadOnlyList<string> argv,
        Func<HttpRequestMessage, Task<HttpResponseMessage>>? send = null)
    {
        if (!env.TryGetValue("AWS_ACCESS_KEY_ID", out var accessKey) || string.IsNullOrEmpty(accessKey))
            return;
        if (!env.TryGetValue("AWS_SECRET_ACCESS_KEY", out var secret) || string.IsNullOrEmpty(secret))
            return;
        if (KeepsLongTermKey(argv))
            return;

        var session = Mint(accessKey, secret, send ?? DefaultSend);
        env["AWS_ACCESS_KEY_ID"] = session.AccessKeyId;
        env["AWS_SECRET_ACCESS_KEY"] = session.SecretAccessKey;
        env["AWS_SESSION_TOKEN"] = session.SessionToken;
    }

    /// <summary>True when this argv must sign with the long-term key.</summary>
    public static bool KeepsLongTermKey(IReadOnlyList<string> argv)
    {
        for (var i = 0; i < argv.Count - 1; i++)
        {
            if (!argv[i].Equals("sts", StringComparison.OrdinalIgnoreCase))
                continue;
            if (argv[i + 1].Equals("get-session-token", StringComparison.OrdinalIgnoreCase)
                || argv[i + 1].Equals("get-federation-token", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static (string AccessKeyId, string SecretAccessKey, string SessionToken) Mint(
        string accessKey,
        string secret,
        Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
    {
        var now = DateTimeOffset.UtcNow;
        var amzDate = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var dateStamp = now.UtcDateTime.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        var body = "Action=GetSessionToken&DurationSeconds=" + DurationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "&Version=2011-06-15";
        var authorization = Sign(accessKey, secret, body, amzDate, dateStamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://" + Host + "/");
        request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(ContentType);
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("Authorization", authorization);

        using var response = send(request).ConfigureAwait(false).GetAwaiter().GetResult();
        var xml = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new AwsSessionException();
        return Parse(xml);
    }

    public static string Sign(string accessKey, string secret, string body, string amzDate, string dateStamp)
    {
        var payloadHash = Hex(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        var canonical = string.Join('\n',
            "POST",
            "/",
            "",
            "content-type:" + ContentType,
            "host:" + Host,
            "x-amz-date:" + amzDate,
            "",
            "content-type;host;x-amz-date",
            payloadHash);
        var scope = dateStamp + "/" + Region + "/" + Service + "/aws4_request";
        var toSign = string.Join('\n',
            "AWS4-HMAC-SHA256",
            amzDate,
            scope,
            Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
        var signature = Hex(Hmac(SigningKey(secret, dateStamp), toSign));
        return "AWS4-HMAC-SHA256 Credential=" + accessKey + "/" + scope
            + ", SignedHeaders=content-type;host;x-amz-date, Signature=" + signature;
    }

    private static byte[] SigningKey(string secret, string dateStamp)
    {
        var date = Hmac(Encoding.UTF8.GetBytes("AWS4" + secret), dateStamp);
        var region = Hmac(date, Region);
        var service = Hmac(region, Service);
        return Hmac(service, "aws4_request");
    }

    private static byte[] Hmac(byte[] key, string data)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    private static (string AccessKeyId, string SecretAccessKey, string SessionToken) Parse(string xml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            throw new AwsSessionException();
        }

        var id = Field(doc, "AccessKeyId");
        var secret = Field(doc, "SecretAccessKey");
        var token = Field(doc, "SessionToken");
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(token))
            throw new AwsSessionException();
        return (id, secret, token);
    }

    private static string? Field(XDocument doc, string localName) =>
        doc.Descendants().FirstOrDefault(e => e.Name.LocalName == localName)?.Value;

    private static Task<HttpResponseMessage> DefaultSend(HttpRequestMessage request) =>
        Http.SendAsync(request);
}
