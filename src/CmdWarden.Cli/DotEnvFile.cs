using System.Text.RegularExpressions;

namespace CmdWarden.Cli;

/// <summary>One line of a dotenv file. Key is null for a comment, an empty line, or a line it cannot read.</summary>
public sealed record DotEnvLine(string Raw, string? Key, string? Value, bool Export)
{
    /// <summary>The vault name of a <c>KEY=cw://NAME</c> line, else null.</summary>
    public string? VaultRef => Value is { } v && v.StartsWith(DotEnvFile.RefPrefix, StringComparison.Ordinal) && v.Length > DotEnvFile.RefPrefix.Length
        ? v[DotEnvFile.RefPrefix.Length..]
        : null;
}

/// <summary>
/// #67: read and write dotenv files. <c>KEY=cw://NAME</c> names a vault entry; <c>cw inject --env-file</c>
/// releases it to the child. Other lines pass through as they are.
/// ponytail: no variable expansion and no multi-line values; such a line stays as it is.
/// </summary>
public static partial class DotEnvFile
{
    public const string RefPrefix = "cw://";

    public static IReadOnlyList<DotEnvLine> Parse(string text) =>
        text.Replace("\r\n", "\n").Split('\n').Select(ParseLine).ToList();

    public static DotEnvLine ParseLine(string raw)
    {
        var match = LinePattern().Match(raw);
        if (!match.Success)
            return new DotEnvLine(raw, null, null, false);
        var value = match.Groups["value"].Value.Trim();
        if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value.IndexOf(value[0], 1) is var end and > 0)
            value = value[1..end];
        else if (value.IndexOf(" #", StringComparison.Ordinal) is var hash and >= 0)
            value = value[..hash].TrimEnd();
        return new DotEnvLine(raw, match.Groups["key"].Value, value, match.Groups["export"].Success);
    }

    /// <summary>The line with the value replaced by a reference to the vault entry.</summary>
    public static string RefLine(DotEnvLine line, string vaultName) =>
        $"{(line.Export ? "export " : "")}{line.Key}={RefPrefix}{vaultName}";

    /// <summary>Names that hold a secret, by the same rule as common secret scanners and the Codex env filter.</summary>
    public static bool LooksSecret(string key) => SecretKeyPattern().IsMatch(key);

    [GeneratedRegex(@"^\s*(?<export>export\s+)?(?<key>[A-Za-z_][A-Za-z0-9_.]*)\s*=(?<value>.*)$")]
    private static partial Regex LinePattern();

    [GeneratedRegex(@"KEY|SECRET|TOKEN|PASSWORD|PASSWD|PWD|CREDENTIAL|AUTH|PRIVATE|DSN|CONNECTION|DATABASE_URL", RegexOptions.IgnoreCase)]
    private static partial Regex SecretKeyPattern();
}
