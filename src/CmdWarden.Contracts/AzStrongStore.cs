using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

namespace CmdWarden.Contracts;

/// <summary>
/// Strong az (#26). az keeps its login in its config dir (<c>~/.azure</c>, or <c>AZURE_CONFIG_DIR</c>):
/// the MSAL token cache, the service principal entries, and the account list. Strong mode moves
/// those files into one store under the product root, protected with DPAPI plus a CmdWarden entropy
/// value. A gated run gets a private copy in a run folder, which the Agent names in the child's
/// <c>AZURE_CONFIG_DIR</c>; after the run the store takes the files back and the folder goes.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AzStrongStore
{
    /// <summary>Login state: leaves the stock dir, so a plain az has no login.</summary>
    public static readonly IReadOnlyList<string> LoginFiles =
    [
        "azureProfile.json", "msal_token_cache.bin", "msal_token_cache.json",
        "service_principal_entries.bin", "service_principal_entries.json", "msal_http_cache.bin",
    ];

    /// <summary>Settings a run needs too; they stay in the stock dir as well.</summary>
    public static readonly IReadOnlyList<string> SettingFiles = ["config", "clouds.config"];

    /// <summary>The audit name of the store, like a vault name.</summary>
    public const string AuditName = "az/login";

    private static readonly byte[] Entropy = "CmdWarden.az.strong.v1"u8.ToArray();

    public AzStrongStore(string? productRoot = null, string? stockDir = null)
    {
        Dir = Path.Combine(productRoot ?? ProductPaths.Root(), "az");
        StockDir = stockDir ?? DefaultStockDir();
    }

    public string Dir { get; }
    public string StorePath => Path.Combine(Dir, "login.bin");
    public string RunsDir => Path.Combine(Dir, "runs");
    public string StockDir { get; }

    /// <summary>What az itself reads: <c>AZURE_CONFIG_DIR</c>, else <c>~/.azure</c>.</summary>
    public static string DefaultStockDir() =>
        Environment.GetEnvironmentVariable("AZURE_CONFIG_DIR") is { Length: > 0 } dir
            ? Path.GetFullPath(dir)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".azure");

    public bool Exists => File.Exists(StorePath);

    /// <summary>A login file is back in the stock dir: a plain az logged in again.</summary>
    public bool StockHasLogin() => LoginFiles.Any(f => File.Exists(Path.Combine(StockDir, f)));

    public IReadOnlyDictionary<string, byte[]> Load()
    {
        if (!Exists)
            return new Dictionary<string, byte[]>();
        var plain = ProtectedData.Unprotect(File.ReadAllBytes(StorePath), Entropy, DataProtectionScope.CurrentUser);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, byte[]>>(plain) ?? [];
        }
        finally
        {
            Array.Clear(plain);
        }
    }

    public void Save(IReadOnlyDictionary<string, byte[]> files)
    {
        Directory.CreateDirectory(Dir);
        var plain = JsonSerializer.SerializeToUtf8Bytes(files);
        var temp = StorePath + ".tmp";
        File.WriteAllBytes(temp, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
        Array.Clear(plain);
        File.Move(temp, StorePath, overwrite: true);
    }

    /// <summary>
    /// Harden: take the login and settings files from the stock dir into the store, then delete the
    /// login files there. A stock login newer than the store replaces it. Returns the login files moved.
    /// </summary>
    public IReadOnlyList<string> Migrate()
    {
        var files = new Dictionary<string, byte[]>(Load(), StringComparer.OrdinalIgnoreCase);
        var stockLogin = LoginFiles.Where(f => File.Exists(Path.Combine(StockDir, f))).ToList();
        if (stockLogin.Count > 0)
        {
            foreach (var name in LoginFiles)
                files.Remove(name);
        }
        foreach (var name in stockLogin.Concat(SettingFiles.Where(f => File.Exists(Path.Combine(StockDir, f)))))
            files[name] = File.ReadAllBytes(Path.Combine(StockDir, name));
        Save(files);
        foreach (var name in stockLogin)
            File.Delete(Path.Combine(StockDir, name));
        return stockLogin;
    }

    /// <summary>Unharden: write every file back to the stock dir, then delete the store and the run folders.</summary>
    public IReadOnlyList<string> WriteBack()
    {
        Directory.CreateDirectory(StockDir);
        var files = Load();
        foreach (var (name, bytes) in files)
        {
            var path = Path.Combine(StockDir, name);
            // Settings the person changed in the stock dir since the harden stay.
            if (SettingFiles.Contains(name) && File.Exists(path))
                continue;
            File.WriteAllBytes(path, bytes);
        }
        File.Delete(StorePath);
        if (Directory.Exists(RunsDir))
            Directory.Delete(RunsDir, recursive: true);
        return files.Keys.Where(LoginFiles.Contains).ToList();
    }

    /// <summary>A run folder for one shim process: <c>runs/&lt;pid&gt;-&lt;start ticks&gt;</c>, with the store files in it.</summary>
    public string Materialize(int shimPid, DateTime shimStartUtc)
    {
        var dir = RunDir(shimPid, shimStartUtc);
        Directory.CreateDirectory(dir);
        foreach (var (name, bytes) in Load())
            File.WriteAllBytes(Path.Combine(dir, name), bytes);
        return dir;
    }

    public string RunDir(int shimPid, DateTime shimStartUtc) => Path.Combine(RunsDir, $"{shimPid}-{shimStartUtc.Ticks}");

    /// <summary>
    /// After the run: the store takes the files from the run folder, and the folder goes. A login
    /// file az deleted (logout) leaves the store too.
    /// ponytail: last capture wins when two az runs overlap; merge the MSAL caches if that loses tokens.
    /// </summary>
    public IReadOnlyList<string> Capture(string runDir)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in LoginFiles.Concat(SettingFiles))
        {
            var path = Path.Combine(runDir, name);
            if (File.Exists(path))
                files[name] = File.ReadAllBytes(path);
        }
        Save(files);
        Directory.Delete(runDir, recursive: true);
        return files.Keys.ToList();
    }

    /// <summary>Delete run folders whose shim process is gone; a crashed shim must not leave a login copy.</summary>
    public int SweepRuns(Func<int, DateTime?> processStartUtc)
    {
        if (!Directory.Exists(RunsDir))
            return 0;
        var removed = 0;
        foreach (var dir in Directory.GetDirectories(RunsDir))
        {
            var parts = Path.GetFileName(dir).Split('-');
            var live = parts.Length == 2 && int.TryParse(parts[0], out var pid) && long.TryParse(parts[1], out var ticks)
                && processStartUtc(pid) is { } start && Math.Abs((start - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds) <= 1;
            if (live)
                continue;
            try
            {
                Directory.Delete(dir, recursive: true);
                removed++;
            }
            catch (IOException)
            {
                // In use; the next sweep gets it.
            }
        }
        return removed;
    }
}
