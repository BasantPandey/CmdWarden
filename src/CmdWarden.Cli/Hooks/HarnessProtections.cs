using System.Text.Json;
using System.Text.Json.Nodes;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Scan;

namespace CmdWarden.Cli.Hooks;

/// <summary>
/// #70: turn on the protections that each harness has but leaves off. Claude Code gets Read deny
/// rules for files that hold secrets and the env scrub; Codex gets a shell environment policy that
/// drops secret variables. Each change is recorded, so the remove takes out only what cw added.
/// </summary>
public static class HarnessProtections
{
    public const string ScrubVariable = "CLAUDE_CODE_SUBPROCESS_ENV_SCRUB";
    public const string CodexBlock = "env";

    /// <summary>Files with secrets that the Claude Code file tools must not read.</summary>
    public static readonly string[] ClaudeDenyRules =
    [
        "Read(**/.env)",
        "Read(**/.env.local)",
        "Read(~/.aws/credentials)",
        "Read(~/.config/gh/hosts.yml)",
        "Read(~/.docker/config.json)",
        "Read(~/.git-credentials)",
        "Read(~/.npmrc)",
        "Read(~/.azure/**)",
        "Read(~/.ssh/id_*)",
        // #71: the policy, pins, and shims of CmdWarden itself.
        "Edit(~/AppData/Local/CmdWarden/**)",
        "Write(~/AppData/Local/CmdWarden/**)",
    ];

    private sealed record State(List<string> ClaudeDeny, bool ClaudeScrub);

    private static string StatePath(string? productRoot) => Path.Combine(productRoot ?? ProductPaths.Root(), "harness-protections.json");

    private static State Load(string? productRoot) =>
        File.Exists(StatePath(productRoot)) && JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath(productRoot))) is { } s
            ? s with { ClaudeDeny = s.ClaudeDeny ?? [] }
            : new State([], false);

    private static void Save(State state, string? productRoot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath(productRoot))!);
        File.WriteAllText(StatePath(productRoot), JsonSerializer.Serialize(state));
    }

    /// <summary>Adds the deny rules and the env scrub that are not there yet. Returns what it added.</summary>
    public static IReadOnlyList<string> InstallClaude(string? settingsPath = null, string? productRoot = null)
    {
        settingsPath ??= HookInstaller.ClaudeSettingsPath();
        var root = HookInstaller.Load(settingsPath);
        var permissions = root["permissions"] as JsonObject ?? new JsonObject();
        root["permissions"] = permissions;
        var deny = permissions["deny"] as JsonArray ?? new JsonArray();
        permissions["deny"] = deny;
        var state = Load(productRoot);
        var added = new List<string>();
        foreach (var rule in ClaudeDenyRules.Where(r => !deny.Any(d => (string?)d == r)))
        {
            deny.Add(rule);
            state.ClaudeDeny.Add(rule);
            added.Add(rule);
        }
        var env = root["env"] as JsonObject ?? new JsonObject();
        root["env"] = env;
        if (env[ScrubVariable] is null)
        {
            env[ScrubVariable] = "1";
            state = state with { ClaudeScrub = true };
            added.Add($"env {ScrubVariable}=1");
        }
        if (added.Count > 0)
        {
            HookInstaller.Save(settingsPath, root);
            Save(state, productRoot);
        }
        return added;
    }

    /// <summary>Removes only the rules and the env scrub that <see cref="InstallClaude"/> added.</summary>
    public static IReadOnlyList<string> UninstallClaude(string? settingsPath = null, string? productRoot = null)
    {
        settingsPath ??= HookInstaller.ClaudeSettingsPath();
        var state = Load(productRoot);
        if (!File.Exists(settingsPath) || (state.ClaudeDeny.Count == 0 && !state.ClaudeScrub))
            return [];
        var root = HookInstaller.Load(settingsPath);
        var removed = new List<string>();
        if (root["permissions"]?["deny"] is JsonArray deny)
        {
            foreach (var node in deny.Where(d => state.ClaudeDeny.Contains((string?)d ?? "")).ToList())
            {
                removed.Add((string)node!);
                deny.Remove(node);
            }
        }
        if (state.ClaudeScrub && root["env"] is JsonObject env && (string?)env[ScrubVariable] == "1")
        {
            env.Remove(ScrubVariable);
            removed.Add($"env {ScrubVariable}");
        }
        HookInstaller.Save(settingsPath, root);
        Save(new State([], false), productRoot);
        return removed;
    }

    /// <summary>
    /// The Codex shell environment policy: its default excludes (KEY, SECRET, TOKEN) are back on, and
    /// the vault names and the known token variables are dropped too.
    /// </summary>
    public static CodexConfig.Change InstallCodex(IEnumerable<string> vaultNames, string? configPath = null)
    {
        var names = AmbientEnv.All.Concat(vaultNames.Select(VaultNames.EnvVarName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase);
        var body = "[shell_environment_policy]\nignore_default_excludes = false\n\n[shell_environment_policy.filters]\n"
            + string.Join("\n", names.Select(n => $"{CodexConfig.Literal(n)} = \"exclude\""));
        return CodexConfig.Set(configPath ?? CodexConfig.Path(), CodexBlock, "shell_environment_policy", body);
    }

    public static bool UninstallCodex(string? configPath = null) =>
        CodexConfig.Remove(configPath ?? CodexConfig.Path(), CodexBlock);

    /// <summary><c>cw protect install|uninstall claude|codex</c>.</summary>
    public static int Run(string[] args)
    {
        switch (args)
        {
            case ["install", "claude"]:
                Report("Claude Code protections", InstallClaude(), "added", "already there", HookInstaller.ClaudeSettingsPath());
                return 0;
            case ["uninstall", "claude"]:
                Report("Claude Code protections", UninstallClaude(), "removed", "none from cw", HookInstaller.ClaudeSettingsPath());
                return 0;
            case ["install", "codex"]:
                var change = InstallCodex(SafeVaultNames());
                Console.WriteLine($"Codex shell environment policy: {change switch
                {
                    CodexConfig.Change.Added => "added",
                    CodexConfig.Change.Updated => "updated",
                    CodexConfig.Change.UserOwned => "skipped: your own [shell_environment_policy] table is there",
                    _ => "already there",
                }} ({CodexConfig.Path()})");
                return 0;
            case ["uninstall", "codex"]:
                Console.WriteLine($"Codex shell environment policy: {(UninstallCodex() ? "removed" : "not there")} ({CodexConfig.Path()})");
                return 0;
            default:
                Console.WriteLine("Usage: cw protect install|uninstall claude|codex");
                Console.WriteLine("  Turn on the protections the harness has but leaves off. cw setup runs install.");
                Console.WriteLine("  claude  Read deny rules for files with secrets, and " + ScrubVariable + "=1.");
                Console.WriteLine("  codex   Drop secret variables from the commands Codex runs (shell_environment_policy).");
                Console.WriteLine("  uninstall removes only what cw added.");
                return args.Length > 0 && args[0] is "-h" or "--help" or "help" ? 0 : 1;
        }
    }

    /// <summary>The vault names, or none when Credential Manager cannot be read.</summary>
    public static IReadOnlyList<string> SafeVaultNames()
    {
        try
        {
            return new CredentialVault().ListNames();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            return [];
        }
    }

    private static void Report(string what, IReadOnlyList<string> changed, string yes, string no, string path)
    {
        Console.WriteLine($"{what}: {(changed.Count == 0 ? no : $"{yes} {changed.Count}")} ({path})");
        foreach (var item in changed)
            Console.WriteLine("  " + item);
    }
}
