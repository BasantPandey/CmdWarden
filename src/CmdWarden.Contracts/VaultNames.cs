namespace CmdWarden.Contracts;

/// <summary>
/// Credential Manager target naming for CmdWarden vault entries.
/// </summary>
public static class VaultNames
{
    public const string TargetPrefix = "CmdWarden/secret/";

    /// <summary>Every CmdWarden entry: named secrets and the strong-mode tool stores.</summary>
    public const string ProductPrefix = "CmdWarden/";

    /// <summary>Short name for any CmdWarden target: the secret name, or <c>&lt;tool&gt;/&lt;key&gt;</c>.</summary>
    public static string DisplayName(string targetName) =>
        targetName.StartsWith(TargetPrefix, StringComparison.Ordinal) ? targetName[TargetPrefix.Length..]
        : targetName.StartsWith(ProductPrefix, StringComparison.Ordinal) ? targetName[ProductPrefix.Length..]
        : targetName;

    /// <summary>Credential helper entries (#202), e.g. <c>CmdWarden/docker/</c>. Hidden from the Secrets tab.</summary>
    public static string HelperTargetPrefix(string tool) => "CmdWarden/" + tool + "/";

    /// <summary>CredMan target for one helper entry: <c>CmdWarden/&lt;tool&gt;/&lt;key&gt;</c>.</summary>
    public static string HelperTargetName(string tool, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Helper key is required.", nameof(key));
        if (key.Contains('*') || key.Contains('\n') || key.Contains('\r'))
            throw new ArgumentException("Helper key must not contain '*' or line breaks.", nameof(key));
        return HelperTargetPrefix(tool) + key.Trim();
    }

    public static string TargetName(string secretName)
    {
        if (string.IsNullOrWhiteSpace(secretName))
            throw new ArgumentException("Secret name is required.", nameof(secretName));

        var name = secretName.Trim();
        if (name.StartsWith('+'))
            name = name[1..];

        foreach (var c in name)
        {
            if (!(char.IsLetterOrDigit(c) || c is '_' or '-' or '.'))
                throw new ArgumentException(
                    "Secret name may only contain letters, digits, underscore, dash, or dot.",
                    nameof(secretName));
        }

        return TargetPrefix + name;
    }

    /// <summary>Reverse of <see cref="TargetName"/>: strips the CredMan target prefix back to the logical name.</summary>
    public static string SecretName(string targetName) =>
        targetName.StartsWith(TargetPrefix, StringComparison.Ordinal)
            ? targetName[TargetPrefix.Length..]
            : targetName;

    public static string EnvVarName(string secretName)
    {
        var name = secretName.Trim();
        if (name.StartsWith('+'))
            name = name[1..];
        return name;
    }
}
