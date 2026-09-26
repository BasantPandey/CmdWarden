using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CmdWarden.Contracts.GitHub;

/// <summary>The GitHub App of the person (#40). The private key is in the vault, not here.</summary>
public sealed class GitHubAppConfig
{
    public long AppId { get; set; }
    public string? Slug { get; set; }
    public string? HtmlUrl { get; set; }
    /// <summary>The REST API root. Tests point it at a local server.</summary>
    public string ApiUrl { get; set; } = GitHubApp.DefaultApiUrl;
}

/// <summary>A short-lived installation token for one repo.</summary>
public sealed record GitHubAppToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// Short-lived, repo-scoped GitHub tokens (#40). On an allowed gh run in a github.com repo, the
/// child gets a GitHub App installation token for that one repo. It ends after one hour. The
/// personal token stays the fallback.
/// </summary>
public static class GitHubApp
{
    public const string DefaultApiUrl = "https://api.github.com";
    public const string Host = "github.com";
    public const string VaultTool = "github-app";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>gh commands that work on one repo: the token of that repo covers them.</summary>
    private static readonly HashSet<string> RepoNouns = new(StringComparer.Ordinal)
    {
        "pr", "issue", "run", "workflow", "release", "label", "cache", "browse",
    };

    public static string ConfigPath(string? productRoot = null) => Path.Combine(productRoot ?? ProductPaths.Root(), "github-app.json");

    /// <summary>The vault entry with the private key (PKCS#8 DER).</summary>
    public static string KeyTarget => VaultNames.HelperTargetName(VaultTool, "private-key");

    public static GitHubAppConfig? Load(string? productRoot = null)
    {
        var path = ConfigPath(productRoot);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<GitHubAppConfig>(File.ReadAllText(path), JsonOptions) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static void Save(GitHubAppConfig config, string? productRoot = null)
    {
        var path = ConfigPath(productRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
    }

    /// <summary>The label of an app token on the card and in the audit.</summary>
    public static string TokenLabel(string repo) => $"GitHub App token for {repo}";

    /// <summary>
    /// "owner/repo" when the gh command works on one github.com repo: from -R/--repo, then GH_REPO,
    /// then the remote of the working folder. Null for other commands, other hosts, or no repo.
    /// </summary>
    public static string? RepoFor(IReadOnlyList<string> argv, string? ghRepoEnv, string? workingDirectory)
    {
        var words = argv.Where((a, i) => !a.StartsWith('-') && (i == 0 || argv[i - 1] is not ("-R" or "--repo"))).ToList();
        var repoCommand = words.Count > 0 && (RepoNouns.Contains(words[0]) || (words.Count > 1 && words[0] == "repo" && words[1] == "view" && words.Count == 2));
        if (!repoCommand)
            return null;

        string? flag = null;
        for (var i = 0; i < argv.Count; i++)
        {
            if (argv[i] is "-R" or "--repo" && i + 1 < argv.Count)
                flag = argv[i + 1];
            else if (argv[i].StartsWith("--repo=", StringComparison.Ordinal))
                flag = argv[i]["--repo=".Length..];
        }
        var named = flag ?? (string.IsNullOrWhiteSpace(ghRepoEnv) ? null : ghRepoEnv);
        if (named is not null)
            return ParseRepo(named);

        var info = GitRepoInfo.TryRead(workingDirectory);
        return info is not null && info.RemoteUrls.TryGetValue(info.RemoteFor(info.CurrentBranch), out var url) ? ParseRepo(url) : null;
    }

    /// <summary>owner/repo from owner/repo, github.com/owner/repo, or a github.com git URL; null for other hosts.</summary>
    public static string? ParseRepo(string text)
    {
        var t = text.Trim();
        if (t.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            t = t[..^4];
        string? host = null;
        if (t.Contains("://", StringComparison.Ordinal))
        {
            var uri = t[(t.IndexOf("://", StringComparison.Ordinal) + 3)..];
            var slash = uri.IndexOf('/');
            if (slash < 0)
                return null;
            host = uri[..slash];
            t = uri[(slash + 1)..];
        }
        else if (t.Contains('@') && t.Contains(':'))
        {
            // git@github.com:owner/repo
            host = t[(t.IndexOf('@') + 1)..t.IndexOf(':')];
            t = t[(t.IndexOf(':') + 1)..];
        }
        if (host is not null)
            host = host[(host.LastIndexOf('@') + 1)..].Split(':')[0];
        var parts = t.Trim('/').Split('/');
        if (parts.Length == 3 && host is null)
            (host, parts) = (parts[0], parts[1..]);
        if (parts.Length != 2 || parts.Any(p => p.Length == 0 || !p.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')))
            return null;
        return host is null || host.Equals(Host, StringComparison.OrdinalIgnoreCase) ? $"{parts[0]}/{parts[1]}" : null;
    }

    /// <summary>An RS256 JWT for the app, valid from one minute ago for nine minutes.</summary>
    public static string CreateJwt(long appId, RSA key, DateTimeOffset now)
    {
        static string B64(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var header = B64("""{"alg":"RS256","typ":"JWT"}"""u8.ToArray());
        var payload = B64(Encoding.UTF8.GetBytes(
            $$"""{"iat":{{now.AddSeconds(-60).ToUnixTimeSeconds()}},"exp":{{now.AddMinutes(9).ToUnixTimeSeconds()}},"iss":"{{appId}}"}"""));
        var signature = key.SignData(Encoding.ASCII.GetBytes(header + "." + payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return header + "." + payload + "." + B64(signature);
    }

    /// <summary>
    /// A token for one repo: find the installation of the app on the repo, then ask for a token
    /// with only that repo. Null when the app is not installed on the repo.
    /// </summary>
    public static async Task<GitHubAppToken?> CreateRepoTokenAsync(HttpClient http, GitHubAppConfig config, RSA key, string repo, CancellationToken ct)
    {
        var jwt = CreateJwt(config.AppId, key, DateTimeOffset.UtcNow);
        using var find = Request(HttpMethod.Get, config, $"repos/{repo}/installation", jwt);
        using var found = await http.SendAsync(find, ct).ConfigureAwait(false);
        if (found.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        await EnsureOkAsync(found, "find the app installation", ct).ConfigureAwait(false);
        var installation = (await found.Content.ReadFromJsonAsync<JsonObject>(ct).ConfigureAwait(false))?["id"]?.GetValue<long>()
            ?? throw new InvalidDataException("GitHub gave no installation id.");

        using var create = Request(HttpMethod.Post, config, $"app/installations/{installation}/access_tokens", jwt);
        create.Content = new StringContent(new JsonObject { ["repositories"] = new JsonArray(repo.Split('/')[1]) }.ToJsonString(),
            Encoding.UTF8, "application/json");
        using var created = await http.SendAsync(create, ct).ConfigureAwait(false);
        await EnsureOkAsync(created, "create the token", ct).ConfigureAwait(false);
        var body = await created.Content.ReadFromJsonAsync<JsonObject>(ct).ConfigureAwait(false);
        var token = body?["token"]?.GetValue<string>() ?? throw new InvalidDataException("GitHub gave no token.");
        var expires = body["expires_at"]?.GetValue<DateTimeOffset>() ?? throw new InvalidDataException("GitHub gave no expiry.");
        return new GitHubAppToken(token, expires);
    }

    /// <summary>The app as GitHub knows it: slug and page. Checks the app id and the key.</summary>
    public static async Task<(string Slug, string HtmlUrl)> GetAppAsync(HttpClient http, GitHubAppConfig config, RSA key, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Get, config, "app", CreateJwt(config.AppId, key, DateTimeOffset.UtcNow));
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureOkAsync(response, "read the app", ct).ConfigureAwait(false);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(ct).ConfigureAwait(false);
        return (body?["slug"]?.GetValue<string>() ?? throw new InvalidDataException("GitHub gave no app slug."),
            body["html_url"]?.GetValue<string>() ?? "");
    }

    /// <summary>Finish the manifest flow: GitHub gives the new app and its private key once, for the code.</summary>
    public static async Task<ManifestConversion> ConvertManifestAsync(HttpClient http, string apiUrl, string code, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{apiUrl.TrimEnd('/')}/app-manifests/{Uri.EscapeDataString(code)}/conversions");
        Headers(request);
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureOkAsync(response, "create the app from the manifest", ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<ManifestConversion>(ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("GitHub gave no app.");
    }

    public sealed record ManifestConversion(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("slug")] string Slug,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("pem")] string Pem);

    /// <summary>The manifest for a new app: repo contents, pull requests, issues and actions; no webhook.</summary>
    public static string Manifest(string name, string redirectUrl) => new JsonObject
    {
        ["name"] = name,
        ["url"] = "https://basantpandey.github.io/CmdWarden/",
        ["redirect_url"] = redirectUrl,
        ["public"] = false,
        ["hook_attributes"] = new JsonObject { ["url"] = "https://basantpandey.github.io/CmdWarden/", ["active"] = false },
        ["default_permissions"] = new JsonObject
        {
            ["contents"] = "write",
            ["pull_requests"] = "write",
            ["issues"] = "write",
            ["actions"] = "write",
            ["metadata"] = "read",
        },
    }.ToJsonString();

    private static HttpRequestMessage Request(HttpMethod method, GitHubAppConfig config, string path, string jwt)
    {
        var request = new HttpRequestMessage(method, $"{config.ApiUrl.TrimEnd('/')}/{path}");
        Headers(request);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return request;
    }

    private static void Headers(HttpRequestMessage request)
    {
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd($"{ProductInfo.Name}/{ProductInfo.Version}");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    private static async Task EnsureOkAsync(HttpResponseMessage response, string what, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var message = JsonNode.Parse(body.Length > 0 && body.TrimStart().StartsWith('{') ? body : "{}")?["message"]?.GetValue<string>();
        throw new HttpRequestException($"GitHub refused to {what}: {(int)response.StatusCode} {message ?? response.ReasonPhrase}".TrimEnd());
    }
}
