using System.Runtime.Versioning;
using System.Text;
using CmdWarden.Contracts;

namespace CmdWarden.Cli.Harden;

public sealed class GitStrongOptions
{
    public string? ProductRoot { get; init; }
    /// <summary>Tests only: GIT_CONFIG_GLOBAL for every git config call.</summary>
    public string? GlobalConfigPath { get; init; }
    /// <summary>Directory that may hold <c>.git-credentials</c>; default the user profile.</summary>
    public string? HomeDir { get; init; }
}

public sealed class GitStrongResult
{
    public required IReadOnlyList<string> MigratedKeys { get; init; }
    public required string HelperValue { get; init; }
    public required IReadOnlyList<string> PreviousHelpers { get; init; }
    public required IReadOnlyList<ConfigLine> RemovedGhBlocks { get; init; }
    public required string Namespace { get; init; }
}

public sealed class GitUnhardenResult
{
    public required IReadOnlyList<string> RestoredKeys { get; init; }
    public IReadOnlyList<string>? RestoredHelpers { get; init; }
    public bool PinRemoved { get; init; }
    public bool ShimRemoved { get; init; }
    public bool HelperRemoved { get; init; }
}

/// <summary>
/// cw harden git --strong (#207): move every GCM entry into the vault and make CmdWarden the only
/// helper in the global config. Order: fail-closed checks, save all, delete legacy, write config.
/// Any failure puts the originals back and removes the vault copies made in this run.
/// </summary>
[SupportedOSPlatform("windows")]
public static class GitStrongHarden
{
    private const string Tool = "git";
    public const string DefaultNamespace = "git";

    public static GitStrongResult Migrate(GitStrongOptions options)
    {
        var root = options.ProductRoot ?? ProductPaths.Root();
        var pins = new ToolPinStore(root);
        var pin = pins.TryGet(Tool) ?? throw new InvalidOperationException("git is not pinned. Run cw harden git first.");
        var helperExe = Path.Combine(root, "shims", HelperTools.GitHelperExe);
        if (!File.Exists(helperExe))
            throw new InvalidOperationException($"{HelperTools.GitHelperExe} is missing from {Path.GetDirectoryName(helperExe)}. Run cw harden git first.");

        var config = new GitGlobalConfig(pin.Path, options.GlobalConfigPath);
        FailClosed(config, options.HomeDir);
        var ns = Namespace(config);
        var vault = new CredentialVault();

        var legacy = ReadLegacy(vault, ns);
        var created = new List<string>();
        var erased = new List<(string Target, VaultEntry Entry)>();
        var previousHelpers = config.GetAll(GitGlobalConfig.HelperKey);
        var ghBlocks = GhBlocks(config.GetScopedHelpers());
        var helperValue = GitGlobalConfig.ShPath(helperExe);
        var configWritten = false;
        try
        {
            foreach (var (target, entry) in legacy)
            {
                var vaultTarget = GitVaultNames.Target(target[(ns.Length + 1)..]);
                var secret = Encoding.UTF8.GetBytes(Encoding.Unicode.GetString(entry.Blob));
                if (vault.ReadTarget(vaultTarget) is { } existing)
                {
                    if (existing.Blob.AsSpan().SequenceEqual(secret))
                        continue;
                    throw new InvalidOperationException(
                        $"vault already holds a different value for {target[(ns.Length + 1)..]}. Nothing was changed.");
                }
                vault.SaveTarget(vaultTarget, entry.UserName, secret, comment: "");
                created.Add(vaultTarget);
            }

            foreach (var (target, entry) in legacy)
            {
                vault.DeleteTarget(target);
                erased.Add((target, entry));
            }

            configWritten = true;
            config.ReplaceAll(GitGlobalConfig.HelperKey, "");
            config.Add(GitGlobalConfig.HelperKey, helperValue);
            foreach (var key in ghBlocks.Select(b => b.Key).Distinct(StringComparer.OrdinalIgnoreCase))
                config.UnsetAll(key);
        }
        catch (Exception ex)
        {
            throw Rollback(vault, ex, erased, created, configWritten ? (config, previousHelpers, ghBlocks) : null, ns);
        }

        var current = pins.TryGet(Tool)!;
        var state = current.IsStrong ? current.Strong ?? new StrongState() : new StrongState();
        state.PreviousHelpers ??= previousHelpers.ToList();
        state.GhHelperBlocks ??= new List<ConfigLine>();
        foreach (var group in ghBlocks.GroupBy(b => b.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!state.GhHelperBlocks.Any(b => b.Key.Equals(group.Key, StringComparison.OrdinalIgnoreCase)))
                state.GhHelperBlocks.AddRange(group);
        }
        pins.SetMode(Tool, ToolPin.StrongMode, strong: state);

        return new GitStrongResult
        {
            MigratedKeys = legacy.Select(l => l.Target[(ns.Length + 1)..]).ToList(),
            HelperValue = helperValue,
            PreviousHelpers = previousHelpers,
            RemovedGhBlocks = ghBlocks,
            Namespace = ns,
        };
    }

    /// <summary>Stores CmdWarden cannot migrate stop the harden before any write.</summary>
    private static void FailClosed(GitGlobalConfig config, string? homeDir)
    {
        var store = Environment.GetEnvironmentVariable("GCM_CREDENTIAL_STORE");
        if (string.IsNullOrWhiteSpace(store))
            store = config.Get("credential.credentialStore");
        if (store is not null && store.Trim().ToLowerInvariant() is "dpapi" or "plaintext")
            throw new InvalidOperationException(
                $"credential.credentialStore is '{store.Trim()}'. CmdWarden migrates the Windows Credential Manager store only. " +
                "Move the credentials to wincredman or remove that store, then run cw harden git --strong again (scan rank 3).");

        var home = homeDir
            ?? Environment.GetEnvironmentVariable("HOME")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var file = Path.Combine(home, ".git-credentials");
        if (File.Exists(file))
            throw new InvalidOperationException(
                $"{file} exists. CmdWarden does not migrate the plaintext store helper. " +
                "Remove the file and the store helper, then run cw harden git --strong again (scan rank 3).");
    }

    public static string Namespace(GitGlobalConfig config)
    {
        var ns = config.Get("credential.namespace");
        return string.IsNullOrWhiteSpace(ns) ? DefaultNamespace : ns.Trim();
    }

    private static List<(string Target, VaultEntry Entry)> ReadLegacy(CredentialVault vault, string ns)
    {
        var list = new List<(string, VaultEntry)>();
        foreach (var t in vault.ListTargets(ns + ":"))
        {
            if (vault.ReadTarget(t.Target) is { } entry && entry.Blob.Length > 0)
                list.Add((t.Target, entry));
        }
        return list;
    }

    /// <summary>Every scoped line of a key that holds a gh helper value, in file order.</summary>
    public static IReadOnlyList<ConfigLine> GhBlocks(IReadOnlyList<ConfigLine> scoped)
    {
        var ghKeys = scoped
            .Where(l => GitGlobalConfig.IsGhHelperValue(l.Value))
            .Select(l => l.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return scoped.Where(l => ghKeys.Contains(l.Key)).ToList();
    }

    private static Exception Rollback(
        CredentialVault vault,
        Exception cause,
        List<(string Target, VaultEntry Entry)> erased,
        List<string> created,
        (GitGlobalConfig Config, IReadOnlyList<string> Helpers, IReadOnlyList<ConfigLine> GhBlocks)? config,
        string ns)
    {
        var failures = new List<Exception>();
        foreach (var (target, entry) in erased)
        {
            try
            {
                vault.SaveTarget(target, entry.UserName, entry.Blob, comment: entry.Comment);
            }
            catch (Exception ex)
            {
                failures.Add(new InvalidOperationException($"could not restore {target}: {ex.Message}", ex));
            }
        }
        if (config is { } c)
        {
            try
            {
                RestoreConfig(c.Config, c.Helpers, c.GhBlocks);
            }
            catch (Exception ex)
            {
                failures.Add(new InvalidOperationException("could not restore the global git config: " + ex.Message, ex));
            }
        }
        if (failures.Count == 0)
        {
            foreach (var target in created)
                vault.DeleteTarget(target);
            return cause;
        }
        return new AggregateException(
            $"harden failed and the rollback was partial; the vault still holds the migrated {ns}: copies",
            new[] { cause }.Concat(failures));
    }

    private static void RestoreConfig(GitGlobalConfig config, IReadOnlyList<string> helpers, IReadOnlyList<ConfigLine> ghBlocks)
    {
        config.Set(GitGlobalConfig.HelperKey, helpers);
        foreach (var group in ghBlocks.GroupBy(b => b.Key, StringComparer.OrdinalIgnoreCase))
            config.Set(group.Key, group.Select(b => b.Value).ToList());
    }

    /// <summary>
    /// cw unharden git (#207): a strong install restores the global helper lines and gh blocks,
    /// writes every vault entry back in GCM layout, and deletes CmdWarden/git/*. Then the pin,
    /// shim, and helper go.
    /// </summary>
    public static GitUnhardenResult Unharden(GitStrongOptions options)
    {
        var root = options.ProductRoot ?? ProductPaths.Root();
        var pins = new ToolPinStore(root);
        var pin = pins.TryGet(Tool);
        var restored = new List<string>();
        IReadOnlyList<string>? restoredHelpers = null;

        if (pin?.IsStrong == true)
        {
            var config = new GitGlobalConfig(pin.Path, options.GlobalConfigPath);
            var ns = Namespace(config);
            var vault = new CredentialVault();
            var prefix = GitVaultNames.Prefix;
            foreach (var t in vault.ListTargets(prefix))
            {
                if (vault.ReadTarget(t.Target) is not { } entry)
                    continue;
                var key = t.Target[prefix.Length..];
                var blob = Encoding.Unicode.GetBytes(Encoding.UTF8.GetString(entry.Blob));
                vault.SaveTarget(ns + ":" + key, entry.UserName, blob, comment: "");
                vault.DeleteTarget(t.Target);
                restored.Add(key);
            }

            var state = pin.Strong ?? new StrongState();
            restoredHelpers = state.PreviousHelpers ?? new List<string>();
            RestoreConfig(config, restoredHelpers, state.GhHelperBlocks ?? new List<ConfigLine>());
        }

        var shimsDir = Path.Combine(root, "shims");
        return new GitUnhardenResult
        {
            RestoredKeys = restored,
            RestoredHelpers = restoredHelpers,
            PinRemoved = pins.Delete(Tool),
            ShimRemoved = DeleteIfExists(Path.Combine(shimsDir, "git.exe")),
            HelperRemoved = DeleteIfExists(Path.Combine(shimsDir, HelperTools.GitHelperExe)),
        };
    }

    private static bool DeleteIfExists(string path)
    {
        if (!File.Exists(path))
            return false;
        File.Delete(path);
        return true;
    }
}
