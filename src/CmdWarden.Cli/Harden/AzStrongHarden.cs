using System.Runtime.Versioning;
using CmdWarden.Contracts;

namespace CmdWarden.Cli.Harden;

public sealed record AzStrongResult(string StockDir, string StorePath, IReadOnlyList<string> MovedLoginFiles);

public sealed record AzUnhardenResult(IReadOnlyList<string> RestoredLoginFiles, bool WasStrong, bool PinRemoved, bool ShimRemoved, string StockDir);

/// <summary>
/// <c>cw harden az --strong</c> and <c>cw unharden az</c> (#26): the az login moves from the stock
/// config dir into the CmdWarden store, and back.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AzStrongHarden
{
    /// <summary>After the compat pin and shim: move the login and mark the pin strong.</summary>
    public static AzStrongResult Migrate(string? productRoot = null, AzStrongStore? store = null)
    {
        store ??= new AzStrongStore(productRoot);
        var moved = store.Migrate();
        new ToolPinStore(productRoot ?? ProductPaths.Root()).SetMode(AzHarden.ToolId, ToolPin.StrongMode);
        return new AzStrongResult(store.StockDir, store.StorePath, moved);
    }

    public static AzUnhardenResult Unharden(string? productRoot = null, AzStrongStore? store = null)
    {
        var root = productRoot ?? ProductPaths.Root();
        store ??= new AzStrongStore(root);
        var pins = new ToolPinStore(root);
        var wasStrong = pins.TryGet(AzHarden.ToolId)?.IsStrong == true;
        var restored = wasStrong || store.Exists ? store.WriteBack() : [];
        var shim = Path.Combine(root, "shims", "az.exe");
        var shimRemoved = File.Exists(shim);
        if (shimRemoved)
            File.Delete(shim);
        return new AzUnhardenResult(restored, wasStrong, pins.Delete(AzHarden.ToolId), shimRemoved, store.StockDir);
    }
}
