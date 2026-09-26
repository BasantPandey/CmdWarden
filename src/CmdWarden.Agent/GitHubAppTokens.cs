using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using CmdWarden.Contracts;
using CmdWarden.Contracts.GitHub;

namespace CmdWarden.Agent;

/// <summary>
/// Installation tokens of the GitHub App per repo (#40). A token lasts one hour; the agent keeps it
/// until five minutes before it ends, so most gh runs need no call to GitHub.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GitHubAppTokens(CredentialVault vault)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly ConcurrentDictionary<string, GitHubAppToken> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A token for the repo, or null with the reason when the personal token must serve.</summary>
    public GitHubAppToken? TryGet(GitHubAppConfig config, string repo, out string? reason)
    {
        reason = null;
        var key = $"{config.AppId}:{repo}";
        if (_cache.TryGetValue(key, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
            return cached;
        var entry = vault.ReadTarget(GitHubApp.KeyTarget);
        if (entry is null)
        {
            reason = "no GitHub App key in the vault; run cw github app setup";
            return null;
        }
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(entry.Blob, out _);
            var token = GitHubApp.CreateRepoTokenAsync(Http, config, rsa, repo, CancellationToken.None).GetAwaiter().GetResult();
            if (token is null)
            {
                reason = $"the GitHub App is not installed on {repo}";
                return null;
            }
            _cache[key] = token;
            return token;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or CryptographicException or System.Text.Json.JsonException or InvalidDataException or InvalidOperationException or FormatException)
        {
            reason = ex.Message;
            return null;
        }
        finally
        {
            Array.Clear(entry.Blob);
        }
    }
}
