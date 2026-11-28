namespace CmdWarden.Contracts;

/// <summary>
/// Credential Manager target naming for CmdWarden vault entries.
/// </summary>
public static class VaultNames
{
    public const string RootEnvVar = "CW_VAULT_ROOT";

    /// <summary>The fake secret of <c>cw try</c>.</summary>
    public const string TrySecret = "CW_TRY_TOKEN";

    /// <summary>
    /// Every CmdWarden entry: named secrets and the strong-mode tool stores. <c>CW_VAULT_ROOT</c>
    /// moves the whole tree, so a test run never reads or deletes a real vault entry.
    /// </summary>
    public static readonly string ProductPrefix =
        Environment.GetEnvironmentVariable(RootEnvVar) is { Length: > 0 } root && root.EndsWith('/') && !root.Contains('*')
            ? root
            : "CmdWarden/";

    public static readonly string TargetPrefix = ProductPrefix + "secret/";

    /// <summary>Per-folder overrides. Not under <see cref="TargetPrefix"/>, so name lists stay global.</summary>
    public static readonly string ProjectPrefix = ProductPrefix + "project/";

    /// <summary>Short name for any CmdWarden target: the secret name, or <c>&lt;tool&gt;/&lt;key&gt;</c>.</summary>
    public static string DisplayName(string targetName) =>
        targetName.StartsWith(TargetPrefix, StringComparison.Ordinal) ? targetName[TargetPrefix.Length..]
        : targetName.StartsWith(ProductPrefix, StringComparison.Ordinal) ? targetName[ProductPrefix.Length..]
        : targetName;

    /// <summary>Credential helper entries (#202), e.g. <c>CmdWarden/docker/</c>. Hidden from the Secrets tab.</summary>
    public static string HelperTargetPrefix(string tool) => ProductPrefix + tool + "/";

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

    /// <summary>Full path, with no trailing slash except a drive root such as <c>C:\</c>. Null when the path is unusable.</summary>
    public static string? NormalizeFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            var full = Path.GetFullPath(path.Trim());
            if (full.Length > 3)
                full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The parent folder, or null at a root.</summary>
    public static string? ParentFolder(string folder)
    {
        try
        {
            var parent = Path.GetDirectoryName(folder);
            if (string.IsNullOrEmpty(parent) || parent.Equals(folder, StringComparison.OrdinalIgnoreCase))
                return null;
            return parent;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary><c>CmdWarden/project/&lt;NAME&gt;/&lt;full path&gt;</c>.</summary>
    public static string ProjectTarget(string secretName, string folder)
    {
        var name = SecretName(TargetName(secretName));
        if (string.IsNullOrWhiteSpace(folder))
            throw new ArgumentException("Project folder is required.", nameof(folder));
        if (folder.Contains('*') || folder.Contains('\n') || folder.Contains('\r'))
            throw new ArgumentException("Project folder must not contain '*' or a line break.", nameof(folder));
        return ProjectPrefix + name + "/" + folder;
    }

    public static string EnvVarName(string secretName)
    {
        var name = secretName.Trim();
        if (name.StartsWith('+'))
            name = name[1..];
        return name;
    }
}
