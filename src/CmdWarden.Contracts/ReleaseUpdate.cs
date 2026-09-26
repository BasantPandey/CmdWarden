using System.Security.Cryptography;
using System.Text.Json;

namespace CmdWarden.Contracts;

/// <summary>One asset of a GitHub release. <see cref="Sha256"/> is the lowercase hex digest GitHub computed.</summary>
public sealed record ReleaseAsset(string Name, string Url, string? Sha256);

public sealed record LatestRelease(string Tag, string Version, IReadOnlyList<ReleaseAsset> Assets)
{
    public string SetupZipName => $"CmdWarden.{Version}-setup.zip";

    public ReleaseAsset? SetupZip => Assets.FirstOrDefault(a => a.Name == SetupZipName);
}

/// <summary>cw update (#45): find the newest release and download its setup zip with a hash check.</summary>
public static class ReleaseUpdate
{
    public const string Repo = "BasantPandey/CmdWarden";
    /// <summary>Test override for the GitHub API root.</summary>
    public const string ApiUrlEnvVar = "CW_UPDATE_API_URL";

    public static string ApiUrl =>
        Environment.GetEnvironmentVariable(ApiUrlEnvVar) is { Length: > 0 } url ? url.TrimEnd('/') : "https://api.github.com";

    public static async Task<LatestRelease> GetLatestAsync(HttpClient http, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiUrl}/repos/{Repo}/releases/latest");
        request.Headers.UserAgent.ParseAdd($"{ProductInfo.Name}-update");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
    }

    public static LatestRelease Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? throw new InvalidDataException("The release has no tag.");
        var assets = new List<ReleaseAsset>();
        if (root.TryGetProperty("assets", out var list))
        {
            foreach (var a in list.EnumerateArray())
            {
                var digest = a.TryGetProperty("digest", out var d) ? d.GetString() : null;
                assets.Add(new ReleaseAsset(
                    a.GetProperty("name").GetString() ?? "",
                    a.GetProperty("browser_download_url").GetString() ?? "",
                    digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true ? digest[7..].ToLowerInvariant() : null));
            }
        }
        return new LatestRelease(tag, tag.TrimStart('v', 'V'), assets);
    }

    /// <summary>
    /// True when <paramref name="latest"/> is a later release than <paramref name="current"/>.
    /// A release beats a prerelease of the same number. A version that does not parse is never newer.
    /// </summary>
    public static bool IsNewer(string latest, string current)
    {
        static (Version? Core, bool Pre) Split(string v)
        {
            var dash = v.IndexOf('-');
            var core = dash >= 0 ? v[..dash] : v;
            return (Version.TryParse(core, out var parsed) ? parsed : null, dash >= 0);
        }
        var (l, lPre) = Split(latest);
        var (c, cPre) = Split(current);
        if (l is null || c is null)
            return false;
        var order = l.CompareTo(c);
        return order > 0 || (order == 0 && cPre && !lPre);
    }

    /// <summary>
    /// Download <paramref name="asset"/> into <paramref name="directory"/> and check its sha256.
    /// A missing digest or a wrong hash deletes the file and throws.
    /// </summary>
    public static async Task<string> DownloadVerifiedAsync(HttpClient http, ReleaseAsset asset, string directory, CancellationToken ct = default)
    {
        if (asset.Sha256 is not { Length: 64 } expected)
            throw new InvalidDataException($"{asset.Name} has no sha256 digest on the release. The update stops.");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Path.GetFileName(asset.Name));
        using (var request = new HttpRequestMessage(HttpMethod.Get, asset.Url))
        {
            request.Headers.UserAgent.ParseAdd($"{ProductInfo.Name}-update");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(path);
            await response.Content.CopyToAsync(file, ct).ConfigureAwait(false);
        }
        string actual;
        await using (var file = File.OpenRead(path))
            actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct).ConfigureAwait(false));
        if (actual != expected)
        {
            File.Delete(path);
            throw new InvalidDataException($"{asset.Name} has sha256 {actual}, but the release says {expected}. The update stops.");
        }
        return path;
    }
}
