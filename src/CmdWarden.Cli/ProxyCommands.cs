using System.Security.Cryptography;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Proxy;

namespace CmdWarden.Cli;

/// <summary>
/// cw proxy setup|add|remove|list|strict|uninstall (#41). The Session Agent runs the proxy; these
/// commands write its config and make or remove its CA.
/// </summary>
public static class ProxyCommands
{
    public static async Task<int> RunAsync(string[] args, Func<string, string, Task> restartAgent)
    {
        try
        {
            return args switch
            {
                ["setup", .. var rest] => await SetupAsync(rest, restartAgent).ConfigureAwait(false),
                ["add", var name, .. var rest] => Add(name, rest),
                ["remove", var name] => Remove(name),
                ["list"] or ["status"] => List(),
                ["strict", "on" or "off"] => Strict(args[1] == "on"),
                ["uninstall"] => await UninstallAsync(restartAgent).ConfigureAwait(false),
                _ => Usage(),
            };
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException
                                   or InvalidOperationException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"cw proxy: {ex.Message}");
            return 1;
        }
    }

    private static int Usage()
    {
        Console.WriteLine("Usage: cw proxy <command>");
        Console.WriteLine("  setup [--port N] [--trust]     Make the per-user CA and turn the proxy on (127.0.0.1)");
        Console.WriteLine("                                 --trust also adds the CA to your Windows root store (Windows asks)");
        Console.WriteLine("  add <NAME> --host <host> ...   Put the vault entry NAME in place of cw://NAME for these hosts");
        Console.WriteLine("  remove <NAME>                  Stop using NAME in the proxy");
        Console.WriteLine("  list                           Show the port, the CA, the keys and their hosts");
        Console.WriteLine("  strict on|off                  on: a host that no key lists gets 403");
        Console.WriteLine("  uninstall                      Remove the CA and the proxy config");
        Console.WriteLine("Then start the harness with cw launch; it gets HTTPS_PROXY and the CA files.");
        return 1;
    }

    private static async Task<int> SetupAsync(string[] args, Func<string, string, Task> restartAgent)
    {
        var config = KeyProxy.Load() ?? new KeyProxyConfig();
        var trust = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--port" && i + 1 < args.Length && int.TryParse(args[++i], out var port) && port is > 0 and < 65536)
                config.Port = port;
            else if (args[i] is "--trust")
                trust = true;
            else
                return Usage();
        }

        using var existing = ProxyCa.Open(config.CaThumbprint);
        var ca = existing?.Certificate ?? ProxyCa.Create();
        try
        {
            config.CaThumbprint = ca.Thumbprint;
            KeyProxy.WriteCaFiles(ca);
            KeyProxy.Save(config);
            if (trust && !ProxyCa.IsTrusted(ca.Thumbprint))
                ProxyCa.Trust(ca);

            Ui.Title($"{ProductInfo.Name} proxy");
            Ui.Kv("proxy", $"http://127.0.0.1:{config.Port}");
            Ui.Kv("CA", $"{ca.Subject} ({ca.Thumbprint}), key in the CurrentUser\\My store");
            Ui.Kv("CA files", $"{KeyProxy.CaPemPath()} and {KeyProxy.BundlePath()}");
            Ui.Kv("Windows trust", ProxyCa.IsTrusted(ca.Thumbprint) ? "yes (CurrentUser root store)" : "no; cw launch gives the harness the CA files (cw proxy setup --trust for all apps)");
            await restartAgent("proxy", "the proxy").ConfigureAwait(false);
            Ui.Line(Ui.Dim("Next: cw save OPENAI_API_KEY, then cw proxy add OPENAI_API_KEY --host api.openai.com"));
            Ui.Line(Ui.Dim("Then: cw launch <harness>; the harness uses cw://OPENAI_API_KEY as its key."));
            return 0;
        }
        finally
        {
            if (existing is null)
                ca.Dispose();
        }
    }

    private static int Add(string name, string[] args)
    {
        var config = KeyProxy.Load() ?? throw new InvalidOperationException("No proxy yet. Run cw proxy setup first.");
        VaultNames.TargetName(name);
        var hosts = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--host" && i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1]))
                hosts.Add(args[++i].Trim().ToLowerInvariant());
            else
                return Usage();
        }
        if (hosts.Count == 0)
            return Usage();
        var key = config.Keys.TryGetValue(name, out var existing) ? existing : config.Keys[name] = new ProxyKey();
        key.Hosts = key.Hosts.Union(hosts, StringComparer.OrdinalIgnoreCase).ToList();
        KeyProxy.Save(config);
        Ui.Kv($"cw://{name}", string.Join(", ", key.Hosts));
        if (!new CredentialVault().ListNames().Contains(name, StringComparer.OrdinalIgnoreCase))
            Ui.Line($"{Ui.Warn("note:")} {Ui.E(name)} is not in the vault yet. Save it: cw save {Ui.E(name)}");
        return 0;
    }

    private static int Remove(string name)
    {
        var config = KeyProxy.Load() ?? new KeyProxyConfig();
        var removed = config.Keys.Remove(name);
        KeyProxy.Save(config);
        Ui.Kv($"cw://{name}", removed ? "removed" : "not in the proxy");
        return 0;
    }

    private static int List()
    {
        var config = KeyProxy.Load();
        Ui.Title($"{ProductInfo.Name} proxy");
        if (config is null)
        {
            Ui.Kv("proxy", "off; run cw proxy setup");
            return 0;
        }
        Ui.Kv("proxy", $"http://127.0.0.1:{config.Port}{(config.Strict ? " (strict: other hosts get 403)" : "")}");
        Ui.Kv("CA", config.CaThumbprint is null ? "none" : $"{config.CaThumbprint}{(ProxyCa.IsTrusted(config.CaThumbprint) ? ", trusted by Windows" : "")}");
        if (config.Keys.Count == 0)
            Ui.Kv("keys", "none; cw proxy add <NAME> --host <host>");
        foreach (var (name, key) in config.Keys.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            Ui.Kv($"cw://{name}", string.Join(", ", key.Hosts));
        return 0;
    }

    private static int Strict(bool on)
    {
        var config = KeyProxy.Load() ?? throw new InvalidOperationException("No proxy yet. Run cw proxy setup first.");
        config.Strict = on;
        KeyProxy.Save(config);
        Ui.Kv("strict", on ? "on: a host that no key lists gets 403" : "off: other hosts pass through untouched");
        return 0;
    }

    private static async Task<int> UninstallAsync(Func<string, string, Task> restartAgent)
    {
        var config = KeyProxy.Load();
        if (config?.CaThumbprint is { } thumbprint)
            ProxyCa.Remove(thumbprint);
        if (Directory.Exists(KeyProxy.Dir()))
            Directory.Delete(KeyProxy.Dir(), recursive: true);
        Ui.Title($"{ProductInfo.Name} proxy");
        Ui.Kv("proxy", config is null ? "none" : "CA and config removed");
        if (config is not null)
            await restartAgent("Session Agent", "no proxy").ConfigureAwait(false);
        return 0;
    }
}
