using System.Security.Cryptography;
using System.Text;
using CmdWarden.Contracts;

namespace CmdWarden.Cli.Canary;

/// <summary>
/// <c>cw canary install|remove</c> (#29). Fake tokens go where a prompt injection looks first:
/// an AWS profile, a vault entry, and any .env template you name. <c>canaries.json</c> records each
/// one, so the Session Agent can spot a use and <c>remove</c> can undo every change.
/// </summary>
public sealed class CanaryInstaller(CanaryStore store, CredentialVault vault, string home, string vaultName = CanaryInstaller.DefaultVaultName)
{
    public const string DefaultVaultName = "GH_TOKEN_BACKUP";
    public const string AwsProfile = "backup-admin";

    public string AwsCredentialsPath => Path.Combine(home, ".aws", "credentials");

    /// <summary>Adds each canary that is not there yet. Returns the new entries.</summary>
    public IReadOnlyList<CanaryEntry> Install(IEnumerable<string> envFiles)
    {
        var entries = store.Load().ToList();
        var added = new List<CanaryEntry>();
        bool Has(string kind, string location) =>
            entries.Any(e => e.Kind == kind && string.Equals(e.Location, location, StringComparison.OrdinalIgnoreCase));

        if (!Has(CanaryStore.VaultKind, vaultName))
        {
            var target = VaultNames.TargetName(vaultName);
            if (vault.ReadTarget(target) is not null)
                throw new InvalidOperationException($"The vault already holds {vaultName}. Nothing was changed.");
            var token = GitHubToken();
            vault.SaveTarget(target, null, Encoding.UTF8.GetBytes(token));
            added.Add(new CanaryEntry(CanaryStore.VaultKind, vaultName, "", [new CanaryToken(vaultName, token)]));
        }

        if (!Has(CanaryStore.FileKind, AwsCredentialsPath))
        {
            if (File.Exists(AwsCredentialsPath) && File.ReadAllText(AwsCredentialsPath).Contains($"[{AwsProfile}]", StringComparison.Ordinal))
                throw new InvalidOperationException($"{AwsCredentialsPath} already has a [{AwsProfile}] profile. Nothing was changed.");
            var keyId = "AKIA" + Random("ABCDEFGHIJKLMNOPQRSTUVWXYZ234567", 16);
            var secret = Random("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/", 40);
            added.Add(AppendBlock(AwsCredentialsPath,
                [$"[{AwsProfile}]", $"aws_access_key_id = {keyId}", $"aws_secret_access_key = {secret}"],
                [new CanaryToken("AWS_ACCESS_KEY_ID", keyId), new CanaryToken("AWS_SECRET_ACCESS_KEY", secret)]));
        }

        foreach (var file in envFiles.Select(Path.GetFullPath))
        {
            if (Has(CanaryStore.FileKind, file))
                continue;
            var token = GitHubToken();
            added.Add(AppendBlock(file, [$"GITHUB_TOKEN={token}"], [new CanaryToken("GITHUB_TOKEN", token)]));
        }

        store.Save([.. entries, .. added]);
        return added;
    }

    /// <summary>Removes every canary. Returns the locations it cleaned.</summary>
    public IReadOnlyList<string> Remove()
    {
        var cleaned = new List<string>();
        foreach (var entry in store.Load())
        {
            if (entry.Kind == CanaryStore.VaultKind)
            {
                var target = VaultNames.TargetName(entry.Location);
                var value = vault.ReadTarget(target) is { } e ? Encoding.UTF8.GetString(e.Blob) : null;
                // Only our fake value: a real secret someone saved later under the same name stays.
                if (value is not null && entry.Tokens.Any(t => t.Value == value))
                    vault.DeleteTarget(target);
            }
            else if (File.Exists(entry.Location))
            {
                RemoveBlock(entry);
            }
            cleaned.Add(entry.Location);
        }
        store.Save([]);
        return cleaned;
    }

    private static CanaryEntry AppendBlock(string path, string[] lines, IReadOnlyList<CanaryToken> tokens)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var existing = File.Exists(path) ? File.ReadAllText(path) : "";
        var nl = existing.Contains("\r\n", StringComparison.Ordinal) || existing.Length == 0 ? "\r\n" : "\n";
        var lead = existing.Length == 0 || existing.EndsWith('\n') ? "" : nl;
        var block = lead + string.Join(nl, lines) + nl;
        File.AppendAllText(path, block);
        return new CanaryEntry(CanaryStore.FileKind, path, block, tokens);
    }

    /// <summary>Takes the exact block out. When someone edited it, drops each line that holds a canary value.</summary>
    private static void RemoveBlock(CanaryEntry entry)
    {
        var text = File.ReadAllText(entry.Location);
        var at = text.LastIndexOf(entry.Block, StringComparison.Ordinal);
        if (at >= 0)
        {
            text = text.Remove(at, entry.Block.Length);
        }
        else
        {
            var nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            text = string.Join(nl, text.Split(nl).Where(l => !entry.Tokens.Any(t => l.Contains(t.Value, StringComparison.Ordinal))));
        }
        if (string.IsNullOrWhiteSpace(text))
            File.Delete(entry.Location);
        else
            File.WriteAllText(entry.Location, text);
    }

    private static string GitHubToken() => "ghp_" + Random("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789", 36);

    private static string Random(string alphabet, int length) =>
        new(RandomNumberGenerator.GetItems<char>(alphabet, length));
}
