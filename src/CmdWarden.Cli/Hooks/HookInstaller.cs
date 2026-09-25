using System.Text.Json;
using System.Text.Json.Nodes;

namespace CmdWarden.Cli.Hooks;

/// <summary>
/// Adds or removes the CmdWarden hook entries in the user config of Claude Code
/// (<c>~/.claude/settings.json</c>) and Cursor (<c>~/.cursor/hooks.json</c>). Other keys stay as they are.
/// </summary>
public static class HookInstaller
{
    public static readonly string[] CursorEvents = ["beforeReadFile", "postToolUse", "afterShellExecution"];

    public static string ClaudeSettingsPath(string? home = null) =>
        Path.Combine(home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    public static string CursorHooksPath(string? home = null) =>
        Path.Combine(home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cursor", "hooks.json");

    /// <summary>
    /// The command line that runs this cw with <paramref name="args"/>. Forward slashes work in
    /// cmd, PowerShell, and bash. A path with a space gets quotes.
    /// ponytail: quotes suit cmd and bash; a PowerShell host needs "&amp;" before a quoted path.
    /// </summary>
    public static string SelfCommand(string args) =>
        string.Join(' ', SelfCommandParts().Select(Quote)) + " " + args;

    /// <summary>How to start this cw: its path, or the dotnet host and cw.dll.</summary>
    public static IReadOnlyList<string> SelfCommandParts()
    {
        var process = Environment.ProcessPath ?? "cw";
        return Path.GetFileNameWithoutExtension(process).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? [process, typeof(HookInstaller).Assembly.Location]
            : [process];
    }

    private static string Quote(string path)
    {
        path = path.Replace('\\', '/');
        return path.Contains(' ') ? $"\"{path}\"" : path;
    }

    /// <summary>Adds one PostToolUse entry for every tool. Returns false when it is already there.</summary>
    public static bool InstallClaude(string settingsPath, string command)
    {
        var root = Load(settingsPath);
        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        root["hooks"] = hooks;
        var list = hooks["PostToolUse"] as JsonArray ?? new JsonArray();
        hooks["PostToolUse"] = list;
        if (list.Any(g => g?["hooks"] is JsonArray inner && inner.Any(h => IsOurs(h?["command"]))))
            return false;
        list.Add(new JsonObject
        {
            ["matcher"] = "*",
            ["hooks"] = new JsonArray(new JsonObject
            {
                ["type"] = "command",
                ["command"] = command,
                ["timeout"] = 30,
            }),
        });
        Save(settingsPath, root);
        return true;
    }

    public static bool UninstallClaude(string settingsPath)
    {
        if (!File.Exists(settingsPath) || Load(settingsPath) is not { } root || root["hooks"]?["PostToolUse"] is not JsonArray list)
            return false;
        var removed = false;
        foreach (var group in list.ToList())
        {
            if (group?["hooks"] is not JsonArray inner)
                continue;
            foreach (var h in inner.Where(h => IsOurs(h?["command"])).ToList())
                removed |= inner.Remove(h);
            if (inner.Count == 0)
                list.Remove(group);
        }
        if (removed)
            Save(settingsPath, root);
        return removed;
    }

    public static bool InstallCursor(string hooksPath, string command)
    {
        var root = Load(hooksPath);
        root["version"] ??= 1;
        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        root["hooks"] = hooks;
        var added = false;
        foreach (var name in CursorEvents)
        {
            var list = hooks[name] as JsonArray ?? new JsonArray();
            hooks[name] = list;
            if (list.Any(h => IsOurs(h?["command"])))
                continue;
            list.Add(new JsonObject { ["command"] = command });
            added = true;
        }
        if (added)
            Save(hooksPath, root);
        return added;
    }

    public static bool UninstallCursor(string hooksPath)
    {
        if (!File.Exists(hooksPath) || Load(hooksPath)["hooks"] is not JsonObject hooks)
            return false;
        var root = hooks.Root.AsObject();
        var removed = false;
        foreach (var (_, value) in hooks.ToList())
        {
            if (value is not JsonArray list)
                continue;
            foreach (var h in list.Where(h => IsOurs(h?["command"])).ToList())
                removed |= list.Remove(h);
        }
        if (removed)
            Save(hooksPath, root);
        return removed;
    }

    private static bool IsOurs(JsonNode? command) =>
        command is JsonValue v && v.TryGetValue<string>(out var text) && text.Contains(" leak-guard ", StringComparison.Ordinal);

    private static JsonObject Load(string path)
    {
        if (!File.Exists(path))
            return new JsonObject();
        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text))
            return new JsonObject();
        return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        }) as JsonObject ?? throw new InvalidDataException($"{path} does not hold a JSON object.");
    }

    private static void Save(string path, JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".cw-tmp";
        File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        File.Move(temp, path, overwrite: true);
    }
}
