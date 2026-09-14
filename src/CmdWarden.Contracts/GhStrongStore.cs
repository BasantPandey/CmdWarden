using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace CmdWarden.Contracts;

public sealed record GhMigrateResult(IReadOnlyList<string> Migrated, IReadOnlyList<string> Deleted, bool HostsStripped);

/// <summary>
/// Strong gh store (#208): the vault holds every gh token under <c>CmdWarden/gh/</c> and stock gh
/// holds none. <see cref="Migrate"/> moves stock entries in (verify, save, delete, strip);
/// <see cref="Reconcile"/> drops vault entries that <c>hosts.yml</c> no longer lists;
/// <see cref="WriteBack"/> restores the stock layout for unharden. Prefixes are overridable so
/// tests run on a private CredMan namespace.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GhStrongStore
{
    private readonly CredentialVault _vault;
    public string VaultPrefix { get; }
    public string StockPrefix { get; }
    public string HostsPath { get; }

    public GhStrongStore(CredentialVault? vault = null, string? vaultPrefix = null, string? stockPrefix = null, string? hostsPath = null)
    {
        _vault = vault ?? new CredentialVault();
        VaultPrefix = vaultPrefix ?? GhVaultNames.Prefix;
        // CW_GH_STOCK_PREFIX: tests point the Agent at a private stock namespace.
        var envPrefix = Environment.GetEnvironmentVariable("CW_GH_STOCK_PREFIX");
        StockPrefix = stockPrefix ?? (string.IsNullOrEmpty(envPrefix) ? GhVaultNames.StockPrefix : envPrefix);
        HostsPath = hostsPath ?? GhHostsFile.DefaultPath();
    }

    public string Target(string user, string host) => VaultPrefix + GhVaultNames.Key(user, host);

    /// <summary>Every vault key (user, host) under the prefix. No blobs leave CredMan.</summary>
    public IReadOnlyList<(string User, string Host)> Keys() =>
        _vault.ListTargets(VaultPrefix).Select(t => GhVaultNames.Parse(t.Target[VaultPrefix.Length..])).ToList();

    public IReadOnlyList<GhHostEntry> Hosts() => GhHostsFile.Read(HostsPath);

    public byte[]? Read(string user, string host) => _vault.ReadTarget(Target(user, host))?.Blob;

    /// <summary>Save-if-absent-or-equal. A different existing value throws and changes nothing.</summary>
    public bool Save(string user, string host, ReadOnlySpan<byte> token)
    {
        var target = Target(user, host);
        if (_vault.ReadTarget(target) is { } existing)
        {
            if (existing.Blob.AsSpan().SequenceEqual(token))
                return false;
            throw new InvalidOperationException(
                $"vault already holds a different value for {GhVaultNames.Key(user, host)}. Nothing was changed.");
        }
        _vault.SaveTarget(target, user.Length == 0 ? null : user, token, comment: "");
        return true;
    }

    public bool Delete(string user, string host) => _vault.DeleteTarget(Target(user, host));

    /// <summary>Stock CredMan entries <c>gh:&lt;host&gt;:&lt;user&gt;</c> plus plaintext tokens from hosts.yml.</summary>
    public List<(string User, string Host, byte[] Token, string? StockTarget)> StockEntries()
    {
        var list = new List<(string, string, byte[], string?)>();
        foreach (var t in _vault.ListTargets(StockPrefix))
        {
            if (GhVaultNames.ParseStock(t.Target, StockPrefix) is not { } parsed)
                continue;
            if (_vault.ReadTarget(t.Target) is { } entry && entry.Blob.Length > 0)
                list.Add((parsed.User, parsed.Host, entry.Blob, t.Target));
        }
        foreach (var host in Hosts())
        {
            foreach (var (user, token) in host.Tokens)
                list.Add((user, host.Host, Encoding.UTF8.GetBytes(token), null));
        }
        return list;
    }

    /// <summary>
    /// Verify every stock token with the real gh, save each into the vault, delete the stock
    /// entries, strip <c>oauth_token</c> lines. A failure before the delete removes the vault
    /// copies made in this call.
    /// </summary>
    public GhMigrateResult Migrate(string realGhPath, bool verify = true)
    {
        var stock = StockEntries();
        var created = new List<(string User, string Host)>();
        try
        {
            foreach (var (user, host, token, _) in stock)
            {
                if (verify && !VerifyToken(realGhPath, host, token))
                    throw new InvalidOperationException(
                        $"gh auth status --hostname {host} failed for {GhVaultNames.Key(user, host)}. Nothing was changed.");
            }
            foreach (var (user, host, token, _) in stock)
            {
                if (Save(user, host, token))
                    created.Add((user, host));
            }
        }
        catch
        {
            foreach (var (user, host) in created)
                Delete(user, host);
            throw;
        }
        finally
        {
            foreach (var (_, _, token, _) in stock)
                Array.Clear(token);
        }

        var deleted = new List<string>();
        foreach (var target in stock.Select(s => s.StockTarget).Where(t => t is not null).Distinct())
        {
            if (_vault.DeleteTarget(target!))
                deleted.Add(target!);
        }
        var stripped = GhHostsFile.StripFile(HostsPath);
        return new GhMigrateResult(
            stock.Select(s => GhVaultNames.Key(s.User, s.Host)).Distinct().ToList(),
            deleted,
            stripped);
    }

    /// <summary>After a logout: drop vault entries hosts.yml no longer lists. Returns the deleted keys.</summary>
    public IReadOnlyList<string> Reconcile()
    {
        var hosts = Hosts();
        var deleted = new List<string>();
        foreach (var (user, host) in Keys())
        {
            var entry = hosts.FirstOrDefault(h => h.Host == host);
            var keep = user.Length == 0
                ? entry is not null
                : entry is not null && entry.Users.Contains(user, StringComparer.OrdinalIgnoreCase);
            if (!keep && Delete(user, host))
                deleted.Add(GhVaultNames.Key(user, host));
        }
        return deleted;
    }

    /// <summary>
    /// Unharden: write every per-user entry back as <c>gh:&lt;host&gt;:&lt;user&gt;</c>, the active
    /// user's token (hosts.yml) to the <c>gh:&lt;host&gt;:</c> slot, and delete the vault copies.
    /// </summary>
    public IReadOnlyList<string> WriteBack()
    {
        var restored = new List<string>();
        var keys = Keys();
        var hosts = Hosts();
        foreach (var (user, host) in keys)
        {
            // The slot follows hosts.yml's active user when the vault holds that user's entry.
            var active = user.Length == 0 ? hosts.FirstOrDefault(h => h.Host == host)?.ActiveUser : null;
            var source = active is { Length: > 0 } && keys.Contains((active, host)) ? active : user;
            WriteStock(host, user, source, restored);
        }
        foreach (var h in hosts)
        {
            if (h.ActiveUser is { Length: > 0 } && !keys.Contains(("", h.Host)) && keys.Contains((h.ActiveUser, h.Host)))
                WriteStock(h.Host, "", h.ActiveUser, restored);
        }
        foreach (var (user, host) in keys)
            Delete(user, host);
        return restored;
    }

    private void WriteStock(string host, string user, string sourceUser, List<string> restored)
    {
        if (Read(sourceUser, host) is not { } token)
            return;
        var target = GhVaultNames.StockTarget(host, user, StockPrefix);
        _vault.SaveTarget(target, user.Length == 0 ? null : user, token, comment: "");
        restored.Add(target);
        Array.Clear(token);
    }

    /// <summary>Run the real gh with only this token in its env; exit 0 means the token works for the host.</summary>
    public static bool VerifyToken(string realGhPath, string host, byte[] token)
    {
        var psi = new ProcessStartInfo
        {
            FileName = realGhPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { "auth", "status", "--hostname", host })
            psi.ArgumentList.Add(a);
        foreach (var name in new[] { "GH_TOKEN", "GITHUB_TOKEN", "GH_ENTERPRISE_TOKEN", "GITHUB_ENTERPRISE_TOKEN" })
            psi.Environment.Remove(name);
        psi.Environment[GhVaultNames.TokenEnvName(host)] = Encoding.UTF8.GetString(token);
        using var process = Process.Start(psi);
        if (process is null)
            return false;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0;
    }
}
