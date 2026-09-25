using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CmdWarden.Contracts.Scan;

/// <summary>
/// Where one plain value sits in a harness config file (#28): the file, the JSON keys down to the
/// object that holds it, the map (<c>env</c> or <c>headers</c>), and the key. Serialized into
/// <see cref="ScanFinding.Fix"/> so the CLI and the Vault can move it later.
/// </summary>
public sealed record McpSecretLocation(string File, IReadOnlyList<string> ObjectPath, string Map, string Key)
{
    /// <summary>Only an <c>env</c> value of a server that starts a command can go through cw inject.</summary>
    public bool CanMove { get; init; }

    public string ToFix() => JsonSerializer.Serialize(this);

    public static McpSecretLocation? FromFix(string? fix)
    {
        if (string.IsNullOrWhiteSpace(fix))
            return null;
        try
        {
            return JsonSerializer.Deserialize<McpSecretLocation>(fix);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string Describe() => string.Join('.', ObjectPath.Append(Map).Append(Key));
}

/// <summary>
/// Plain tokens in MCP and AI harness config files (#28). GitGuardian found thousands of valid
/// secrets in public MCP configs. The detector names the file and the key, never the value.
/// </summary>
public sealed class McpConfigSecretDetector : IScanDetector
{
    public const string FindingId = "mcp.plain_secret";

    public string Id => FindingId;

    public IReadOnlyList<ScanFinding> Detect(ScanContext context)
    {
        var findings = new List<ScanFinding>();
        foreach (var file in McpConfigFiles.Candidates(context).Where(File.Exists))
        {
            foreach (var location in McpConfigFiles.PlainSecrets(file))
            {
                findings.Add(new ScanFinding(
                    Id: Id,
                    Tool: "mcp",
                    Severity: ScanSeverity.High,
                    Title: "Plain secret in an AI harness config file",
                    Summary: "A config file of an AI harness or MCP server holds a token in plain text. " +
                             "Every process that reads the file gets it, and it can end up in a repository.",
                    Evidence: $"{file}: {location.Describe()} (value not shown)",
                    Remediation: location.CanMove
                        ? "Move to vault: the value goes into the vault, the file gets a placeholder, and the server starts through cw inject."
                        : "Move the value into the vault by hand, and remove it from the file. Headers and remote servers cannot use cw inject.",
                    HardenHint: location.CanMove ? "cw scan --move-to-vault" : null,
                    Fix: location.CanMove ? location.ToFix() : null));
            }
        }
        return findings;
    }
}

public static class McpConfigFiles
{
    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private static readonly string[] SecretPrefixes =
        ["ghp_", "gho_", "ghu_", "ghs_", "ghr_", "github_pat_", "glpat-", "sk-", "xoxb-", "xoxp-", "xapp-", "AKIA", "ASIA", "AIza", "npm_", "pypi-", "hf_", "dop_v1_", "SG."];

    private static readonly string[] SecretNameParts =
        ["TOKEN", "SECRET", "PASSWORD", "PASSWD", "API_KEY", "APIKEY", "ACCESS_KEY", "PRIVATE_KEY", "CREDENTIAL", "AUTH", "_PAT"];

    /// <summary>User and project config files of Claude Code, Claude Desktop, Cursor, and VS Code.</summary>
    public static IReadOnlyList<string> Candidates(ScanContext context)
    {
        var home = context.UserProfile;
        var cwd = context.WorkingDirectory;
        var list = new List<string>
        {
            Path.Combine(home, ".claude.json"),
            Path.Combine(home, ".claude", "settings.json"),
            Path.Combine(home, ".cursor", "mcp.json"),
            Path.Combine(context.AppData, "Claude", "claude_desktop_config.json"),
            Path.Combine(context.AppData, "Code", "User", "mcp.json"),
            Path.Combine(cwd, ".mcp.json"),
            Path.Combine(cwd, ".claude", "settings.json"),
            Path.Combine(cwd, ".claude", "settings.local.json"),
            Path.Combine(cwd, ".cursor", "mcp.json"),
            Path.Combine(cwd, ".vscode", "mcp.json"),
        };
        return list.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Each plain secret value in the file. A file that does not parse has none.</summary>
    public static IReadOnlyList<McpSecretLocation> PlainSecrets(string file)
    {
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(file), documentOptions: ReadOptions) as JsonObject;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
        if (root is null)
            return [];

        var found = new List<McpSecretLocation>();
        // Harness settings: a top-level env block goes to every tool the harness starts.
        Scan(file, root, [], "env", found, canMove: false);
        foreach (var serversKey in new[] { "mcpServers", "servers" })
            ScanServers(file, root[serversKey] as JsonObject, [serversKey], found);
        // ~/.claude.json keeps servers per project too.
        if (root["projects"] is JsonObject projects)
        {
            foreach (var (project, value) in projects)
                ScanServers(file, value?["mcpServers"] as JsonObject, ["projects", project, "mcpServers"], found);
        }
        return found;
    }

    private static void ScanServers(string file, JsonObject? servers, string[] path, List<McpSecretLocation> found)
    {
        if (servers is null)
            return;
        foreach (var (name, server) in servers)
        {
            if (server is not JsonObject obj)
                continue;
            var startsCommand = obj["command"] is JsonValue;
            Scan(file, obj, [.. path, name], "env", found, canMove: startsCommand);
            Scan(file, obj, [.. path, name], "headers", found, canMove: false);
        }
    }

    private static void Scan(string file, JsonObject owner, string[] path, string map, List<McpSecretLocation> found, bool canMove)
    {
        if (owner[map] is not JsonObject values)
            return;
        foreach (var (key, value) in values)
        {
            if (value is JsonValue v && v.TryGetValue<string>(out var text) && LooksSecret(key, text))
                found.Add(new McpSecretLocation(file, path, map, key) { CanMove = canMove && IsEnvName(key) });
        }
    }

    /// <summary>A real value, not a placeholder, that has a known token shape or a secret-like name.</summary>
    public static bool LooksSecret(string name, string value)
    {
        var v = value.Trim();
        if (v.Length < LeakRedactor.MinValueLength || IsPlaceholder(v))
            return false;
        if (v.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || v.StartsWith("token ", StringComparison.OrdinalIgnoreCase))
            return v.Length >= 16 && !IsPlaceholder(v[(v.IndexOf(' ') + 1)..].Trim());
        if (SecretPrefixes.Any(p => v.StartsWith(p, StringComparison.Ordinal)))
            return true;
        var upper = name.ToUpperInvariant();
        return SecretNameParts.Any(upper.Contains) && !v.Contains(' ');
    }

    private static bool IsPlaceholder(string v) =>
        v.StartsWith("${", StringComparison.Ordinal) || v.StartsWith("$env:", StringComparison.OrdinalIgnoreCase)
        || v.StartsWith('%') || v.StartsWith('<') || v.StartsWith("[" + ProductInfo.Name + ":", StringComparison.Ordinal)
        || v.Contains("your", StringComparison.OrdinalIgnoreCase) || v.Trim('x', 'X', '*', '.').Length == 0;

    private static bool IsEnvName(string key) =>
        key.Length > 0 && key.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.');
}

/// <summary>
/// "Move to vault" (#28): save the value in the vault, put a placeholder in the file, and start the
/// server through <c>cw inject</c>, so it gets the value at start and the file holds none.
/// </summary>
[SupportedOSPlatform("windows")]
public static class McpSecretMover
{
    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <param name="cwCommand">How to start cw: its path, or the dotnet host and cw.dll.</param>
    /// <returns>The vault name.</returns>
    public static string Move(McpSecretLocation location, CredentialVault vault, IReadOnlyList<string> cwCommand)
    {
        var root = JsonNode.Parse(File.ReadAllText(location.File), documentOptions: ReadOptions) as JsonObject
            ?? throw new InvalidDataException($"{location.File} does not hold a JSON object.");
        JsonNode? node = root;
        foreach (var key in location.ObjectPath)
            node = node?[key];
        if (node is not JsonObject server || server[location.Map]?[location.Key] is not JsonValue v || !v.TryGetValue<string>(out var value)
            || !McpConfigFiles.LooksSecret(location.Key, value))
            throw new InvalidOperationException($"{location.Describe()} holds no plain secret now. Run the scan again.");
        if (server["command"] is not JsonValue commandNode || !commandNode.TryGetValue<string>(out var command))
            throw new InvalidOperationException($"{location.Describe()}: the server starts no command, so cw inject cannot give it the value.");

        // Save if absent or equal; a different vault value stops the move before the file changes.
        var name = location.Key;
        var target = VaultNames.TargetName(name);
        if (vault.ReadTarget(target) is { } existing)
        {
            var same = CredentialVault.Utf8(existing.Blob) == value;
            Array.Clear(existing.Blob);
            if (!same)
                throw new InvalidOperationException($"The vault already holds a different {name}. Nothing was changed.");
        }
        else
        {
            vault.SaveTarget(target, null, CredentialVault.Utf8Bytes(value));
        }

        server[location.Map]![location.Key] = LeakRedactor.Placeholder(name);
        var args = server["args"] as JsonArray ?? new JsonArray();
        var wrapped = string.Equals(command, cwCommand[0], StringComparison.OrdinalIgnoreCase)
            && args.Select(a => (string?)a).Contains("inject");
        if (wrapped)
        {
            var at = args.Select(a => (string?)a).ToList().IndexOf("inject");
            if (!args.Select(a => (string?)a).Contains("+" + name))
                args.Insert(at + 1, "+" + name);
        }
        else
        {
            var newArgs = new JsonArray();
            foreach (var part in cwCommand.Skip(1))
                newArgs.Add(part);
            foreach (var part in new[] { "inject", "--tool", "mcp", "--class", "read", "+" + name, "--", command })
                newArgs.Add(part);
            foreach (var a in args)
                newArgs.Add(a?.DeepClone());
            server["command"] = cwCommand[0];
            args = newArgs;
        }
        server["args"] = args;

        var temp = location.File + ".cw-tmp";
        File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        File.Move(temp, location.File, overwrite: true);
        return name;
    }
}
