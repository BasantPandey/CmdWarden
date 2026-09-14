using System.Text.Json;
using System.Text.Json.Nodes;

namespace CmdWarden.Contracts;

/// <summary>docker CLI config.json facts shared by harden, unharden, and the doctor probe (#204).</summary>
public static class DockerConfigFile
{
    public const string CmdWardenStore = "cmdwarden";
    /// <summary>Attribute label Docker Desktop and wincred put on every registry credential.</summary>
    public const string LegacyLabel = "Docker Credentials";

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    /// <summary>DOCKER_CONFIG dir when set, else %USERPROFILE%\.docker.</summary>
    public static string DefaultPath()
    {
        var dir = Environment.GetEnvironmentVariable("DOCKER_CONFIG");
        if (string.IsNullOrWhiteSpace(dir))
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".docker");
        return Path.Combine(dir, "config.json");
    }

    /// <summary>The file as a JSON object; a missing file is an empty object.</summary>
    public static JsonObject Read(string path)
    {
        if (!File.Exists(path))
            return new JsonObject();
        return JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidOperationException($"{path} is not a JSON object.");
    }

    public static string? ReadCredsStore(string path) =>
        File.Exists(path) ? Read(path)["credsStore"]?.GetValue<string>() : null;

    /// <summary>
    /// True when <c>credHelpers</c> routes this registry to another helper. Its store entry
    /// belongs to that helper: harden leaves it, and the probe does not count it as returned.
    /// </summary>
    public static bool IsForeignHelperTarget(JsonObject config, string target)
    {
        if (config["credHelpers"] is not JsonObject helpers || helpers.Count == 0)
            return false;
        var host = RegistryHost(target);
        return helpers.Any(h => string.Equals(RegistryHost(h.Key), host, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>docker's registry key compare: scheme off, path off, host plus port kept.</summary>
    public static string RegistryHost(string registry)
    {
        var s = registry.Trim();
        var scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
            s = s[(scheme + 3)..];
        var slash = s.IndexOf('/');
        return slash >= 0 ? s[..slash] : s;
    }

    /// <summary>Temp file in the same directory, then replace. Readers never see a partial file.</summary>
    public static void WriteAtomic(string path, JsonObject config)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, Path.GetFileName(path) + ".cw-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            File.WriteAllText(temp, config.ToJsonString(Pretty));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
        }
    }
}
