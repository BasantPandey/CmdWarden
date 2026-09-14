using System.Runtime.Versioning;
using System.Text;
using System.Text.Json.Nodes;
using CmdWarden.Contracts;

namespace CmdWarden.Cli.Harden;

public sealed class DockerStrongOptions
{
    public string? ProductRoot { get; init; }
    /// <summary>docker config.json; default DOCKER_CONFIG or %USERPROFILE%\.docker\config.json.</summary>
    public string? ConfigPath { get; init; }
    /// <summary>Tests only: touch legacy and inline entries whose URL starts with this prefix.</summary>
    public string? UrlPrefix { get; init; }
}

public sealed class DockerStrongResult
{
    public required IReadOnlyList<string> MigratedUrls { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public string? PreviousCredsStore { get; init; }
    public required string ConfigPath { get; init; }
}

public sealed class DockerUnhardenResult
{
    public required IReadOnlyList<string> RestoredUrls { get; init; }
    public string? RestoredCredsStore { get; init; }
    public bool PinRemoved { get; init; }
    public bool ShimRemoved { get; init; }
    public bool HelperRemoved { get; init; }
}

/// <summary>
/// cw harden docker --strong (#204): move every registry credential into the vault and leave no
/// copy behind. Order: save all, erase legacy, write config. Any failure before the config write
/// puts the originals back and removes the vault copies made in this run.
/// </summary>
[SupportedOSPlatform("windows")]
public static class DockerStrongHarden
{
    private const string Tool = "docker";

    public static DockerStrongResult Migrate(DockerStrongOptions options)
    {
        var root = options.ProductRoot ?? ProductPaths.Root();
        var pins = new ToolPinStore(root);
        if (pins.TryGet(Tool) is null)
            throw new InvalidOperationException("docker is not pinned. Run cw harden docker first.");

        var configPath = options.ConfigPath ?? DockerConfigFile.DefaultPath();
        var config = DockerConfigFile.Read(configPath);
        var vault = new CredentialVault();
        var warnings = new List<string>();

        var (legacy, entries, inlineAuths) = Collect(vault, config, options.UrlPrefix, warnings);

        foreach (var e in entries)
        {
            if (e.Blob.Length == 0)
                throw new InvalidOperationException($"registry credential for {e.Target} has an empty secret. Nothing was changed.");
            if (e.Blob.Length > CredentialVault.MaxBlobBytes)
                throw new InvalidOperationException(
                    $"registry credential for {e.Target} is {e.Blob.Length} bytes; the vault limit is {CredentialVault.MaxBlobBytes}. Nothing was changed.");
        }

        var created = new List<string>();
        var erased = new List<LabeledEntry>();
        string? previous;
        try
        {
            foreach (var e in entries)
            {
                var target = VaultNames.HelperTargetName(Tool, e.Target);
                if (vault.ReadTarget(target) is { } existing)
                {
                    if (existing.UserName == e.UserName && existing.Blob.AsSpan().SequenceEqual(e.Blob))
                        continue;
                    throw new InvalidOperationException($"vault already holds a different value for {e.Target}. Nothing was changed.");
                }
                vault.SaveTarget(target, e.UserName, e.Blob);
                created.Add(target);
            }

            foreach (var l in legacy)
            {
                vault.DeleteTarget(l.Target);
                erased.Add(l);
            }

            foreach (var auth in inlineAuths)
            {
                if (auth.ContainsKey("auth"))
                    auth["auth"] = "";
                if (auth.ContainsKey("identitytoken"))
                    auth["identitytoken"] = "";
            }

            previous = config["credsStore"]?.GetValue<string>();
            if (string.Equals(previous, DockerConfigFile.CmdWardenStore, StringComparison.Ordinal))
                previous = null;
            config["credsStore"] = DockerConfigFile.CmdWardenStore;
            DockerConfigFile.WriteAtomic(configPath, config);
        }
        catch (Exception ex)
        {
            throw Rollback(vault, ex, erased, created);
        }

        // The config now names cmdwarden. A pin write failure here must not undo that.
        var current = pins.TryGet(Tool)!;
        pins.SetMode(Tool, ToolPin.StrongMode, current.IsStrong ? current.PreviousCredsStore : previous);
        return new DockerStrongResult
        {
            MigratedUrls = entries.Select(e => e.Target).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Warnings = warnings,
            PreviousCredsStore = previous,
            ConfigPath = configPath,
        };
    }

    /// <summary>Legacy store entries, inline auths, and the inline nodes to blank. Foreign-helper registries stay.</summary>
    private static (List<LabeledEntry> Legacy, List<LabeledEntry> Entries, List<JsonObject> InlineAuths) Collect(
        CredentialVault vault, JsonObject config, string? urlPrefix, List<string> warnings)
    {
        var legacy = new List<LabeledEntry>();
        foreach (var e in vault.ReadAllWithLabel(DockerConfigFile.LegacyLabel))
        {
            if (!InScope(e.Target, urlPrefix))
                continue;
            if (DockerConfigFile.IsForeignHelperTarget(config, e.Target))
                warnings.Add($"{e.Target} stays in the Docker store: credHelpers routes it to another helper");
            else
                legacy.Add(e);
        }

        var entries = new List<LabeledEntry>(legacy);
        var inlineAuths = new List<JsonObject>();
        if (config["auths"] is JsonObject auths)
        {
            foreach (var (url, node) in auths)
            {
                if (node is not JsonObject auth || !InScope(url, urlPrefix) || DockerConfigFile.IsForeignHelperTarget(config, url))
                    continue;
                if (InlineEntry(url, auth) is { } entry)
                {
                    entries.Add(entry);
                    inlineAuths.Add(auth);
                }
            }
        }
        if (config["credHelpers"] is JsonObject helpers && helpers.Count > 0)
            warnings.Add("credHelpers left in place for: " + string.Join(", ", helpers.Select(h => h.Key)));
        return (legacy, entries, inlineAuths);
    }

    /// <summary>
    /// Put erased legacy entries back, then drop this run's vault copies. The copies stay when a
    /// restore fails, so no credential is lost; every failure is reported with the cause.
    /// </summary>
    private static Exception Rollback(CredentialVault vault, Exception cause, List<LabeledEntry> erased, List<string> created)
    {
        var failures = new List<Exception>();
        foreach (var l in erased)
        {
            try
            {
                vault.WriteWithLabel(l.Target, l.UserName, l.Blob, DockerConfigFile.LegacyLabel);
            }
            catch (Exception ex)
            {
                failures.Add(new InvalidOperationException($"could not restore {l.Target}: {ex.Message}", ex));
            }
        }
        if (failures.Count == 0)
        {
            foreach (var target in created)
                vault.DeleteTarget(target);
            return cause;
        }
        return new AggregateException(
            "harden failed and the rollback was partial; the vault still holds the migrated copies",
            new[] { cause }.Concat(failures));
    }

    /// <summary>
    /// cw unharden docker (#204): a strong install writes every vault entry back in wincred layout,
    /// deletes CmdWarden/docker/*, and restores credsStore. Then the pin, shim, and helper go.
    /// </summary>
    public static DockerUnhardenResult Unharden(DockerStrongOptions options)
    {
        var root = options.ProductRoot ?? ProductPaths.Root();
        var pins = new ToolPinStore(root);
        var pin = pins.TryGet(Tool);
        var restored = new List<string>();
        string? restoredStore = null;

        if (pin?.IsStrong == true)
        {
            var vault = new CredentialVault();
            var prefix = VaultNames.HelperTargetPrefix(Tool);
            foreach (var t in vault.ListTargets(prefix))
            {
                var url = t.Target[prefix.Length..];
                if (!InScope(url, options.UrlPrefix) || vault.ReadTarget(t.Target) is not { } entry)
                    continue;
                vault.WriteWithLabel(url, entry.UserName, entry.Blob, DockerConfigFile.LegacyLabel);
                vault.DeleteTarget(t.Target);
                restored.Add(url);
            }

            var configPath = options.ConfigPath ?? DockerConfigFile.DefaultPath();
            if (File.Exists(configPath) || !string.IsNullOrEmpty(pin.PreviousCredsStore))
            {
                var config = DockerConfigFile.Read(configPath);
                if (string.IsNullOrEmpty(pin.PreviousCredsStore))
                    config.Remove("credsStore");
                else
                    config["credsStore"] = pin.PreviousCredsStore;
                DockerConfigFile.WriteAtomic(configPath, config);
            }
            restoredStore = pin.PreviousCredsStore ?? "";
        }

        var shimsDir = Path.Combine(root, "shims");
        return new DockerUnhardenResult
        {
            RestoredUrls = restored,
            RestoredCredsStore = restoredStore,
            PinRemoved = pins.Delete(Tool),
            ShimRemoved = DeleteIfExists(Path.Combine(shimsDir, "docker.exe")),
            HelperRemoved = DeleteIfExists(Path.Combine(shimsDir, HelperTools.DockerHelperExe)),
        };
    }

    private static bool InScope(string url, string? prefix) =>
        prefix is null || url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>docker file store shapes: <c>auth</c> is base64 user:password; <c>identitytoken</c> maps to user &lt;token&gt;.</summary>
    private static LabeledEntry? InlineEntry(string url, JsonObject auth)
    {
        var token = auth["identitytoken"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(token))
            return new LabeledEntry(url, "<token>", Encoding.UTF8.GetBytes(token));

        var basic = auth["auth"]?.GetValue<string>();
        if (string.IsNullOrEmpty(basic))
            return null;
        var pair = Encoding.UTF8.GetString(Convert.FromBase64String(basic));
        var colon = pair.IndexOf(':');
        if (colon <= 0)
            throw new InvalidOperationException($"inline auth for {url} is not user:password. Nothing was changed.");
        return new LabeledEntry(url, pair[..colon], Encoding.UTF8.GetBytes(pair[(colon + 1)..]));
    }

    private static bool DeleteIfExists(string path)
    {
        if (!File.Exists(path))
            return false;
        File.Delete(path);
        return true;
    }
}
