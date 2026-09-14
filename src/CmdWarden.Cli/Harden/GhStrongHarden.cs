using System.Runtime.Versioning;
using System.Text;
using CmdWarden.Contracts;

namespace CmdWarden.Cli.Harden;

public sealed class GhStrongOptions
{
    public string? ProductRoot { get; init; }
    /// <summary>--token: import this value for <see cref="Hostname"/> instead of the stock store.</summary>
    public string? TokenOverride { get; init; }
    public string Hostname { get; init; } = GhVaultNames.DefaultHost;
    /// <summary>Tests only: a private stock namespace and hosts.yml.</summary>
    public GhStrongStore? Store { get; init; }
}

public sealed record GhStrongResult(IReadOnlyList<string> Migrated, IReadOnlyList<string> Deleted, bool HostsStripped, IReadOnlyList<GhHostState> Hosts);

public sealed record GhUnhardenResult(IReadOnlyList<string> Restored, bool WasStrong, bool PinRemoved, bool ShimRemoved);

/// <summary>
/// cw harden gh --strong (#209): every stock gh token moves into the vault and no copy stays
/// behind. The store does verify, save, delete, strip; the pin document records the hosts.
/// </summary>
[SupportedOSPlatform("windows")]
public static class GhStrongHarden
{
    private const string Tool = "gh";

    public static GhStrongResult Migrate(GhStrongOptions options)
    {
        var root = options.ProductRoot ?? ProductPaths.Root();
        var pins = new ToolPinStore(root);
        var pin = pins.TryGet(Tool) ?? throw new InvalidOperationException("gh is not pinned. Run cw harden gh first.");
        var store = options.Store ?? new GhStrongStore();

        GhMigrateResult result;
        if (options.TokenOverride is { Length: > 0 } token)
        {
            var bytes = Encoding.UTF8.GetBytes(token.Trim());
            try
            {
                if (!GhStrongStore.VerifyToken(pin.Path, options.Hostname, bytes))
                    throw new InvalidOperationException($"gh auth status --hostname {options.Hostname} failed for the given token. Nothing was changed.");
                store.Save("", options.Hostname, bytes);
            }
            finally
            {
                Array.Clear(bytes);
            }
            result = new GhMigrateResult([GhVaultNames.Key("", options.Hostname)], [], false);
        }
        else
        {
            result = store.Migrate(pin.Path);
        }

        var hosts = store.Hosts().Select(h => new GhHostState { Host = h.Host, Users = h.Users.ToList(), ActiveUser = h.ActiveUser }).ToList();
        pins.SetMode(Tool, ToolPin.StrongMode, strong: new StrongState { Hosts = hosts });
        return new GhStrongResult(result.Migrated, result.Deleted, result.HostsStripped, hosts);
    }

    /// <summary>
    /// cw unharden gh: a strong install writes every entry back in stock layout and deletes
    /// CmdWarden/gh/*. The compat GH_TOKEN stays (#166 keep rule). Then the pin and shim go.
    /// </summary>
    public static GhUnhardenResult Unharden(GhStrongOptions options)
    {
        var root = options.ProductRoot ?? ProductPaths.Root();
        var pins = new ToolPinStore(root);
        var pin = pins.TryGet(Tool);
        var restored = pin?.IsStrong == true
            ? (options.Store ?? new GhStrongStore()).WriteBack()
            : Array.Empty<string>();
        var shim = Path.Combine(root, "shims", "gh.exe");
        var shimRemoved = File.Exists(shim);
        if (shimRemoved)
            File.Delete(shim);
        return new GhUnhardenResult(restored, pin?.IsStrong == true, pins.Delete(Tool), shimRemoved);
    }
}
