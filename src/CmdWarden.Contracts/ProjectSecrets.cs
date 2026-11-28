namespace CmdWarden.Contracts;

/// <summary>One vault blob and the CredMan target it came from.</summary>
public readonly record struct SecretBlob(byte[] Bytes, string Target);

/// <summary>
/// A secret may have one global value and a different value per project folder.
/// The nearest folder wins. An empty blob is not a hit.
/// </summary>
public static class ProjectSecrets
{
    /// <summary>Global target, or the project target. A bad project path throws <see cref="ArgumentException"/>.</summary>
    public static string TargetFor(string name, string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return VaultNames.TargetName(name);
        var folder = VaultNames.NormalizeFolder(projectPath)
            ?? throw new ArgumentException("Project path is not a folder path.", nameof(projectPath));
        return VaultNames.ProjectTarget(name, folder);
    }

    /// <summary>
    /// Walk from the working folder to the root. The first non-empty project blob wins, else the global blob.
    /// A bad path or a bad name returns null.
    /// </summary>
    public static SecretBlob? Find(
        string name,
        string? workingDirectory,
        Func<string, byte[]?> readTarget,
        Func<string, byte[]?> readNamed)
    {
        try
        {
            _ = VaultNames.TargetName(name);
        }
        catch (ArgumentException)
        {
            return null;
        }

        for (var folder = VaultNames.NormalizeFolder(workingDirectory); folder is not null; folder = VaultNames.ParentFolder(folder))
        {
            string target;
            try
            {
                target = VaultNames.ProjectTarget(name, folder);
            }
            catch (ArgumentException)
            {
                continue;
            }

            var blob = readTarget(target);
            if (blob is { Length: > 0 })
                return new SecretBlob(blob, target);
            if (blob is { Length: 0 })
                Array.Clear(blob);
        }

        var global = readNamed(name);
        if (global is { Length: > 0 })
            return new SecretBlob(global, VaultNames.TargetName(name));
        if (global is { Length: 0 })
            Array.Clear(global);
        return null;
    }

    /// <summary>True when <see cref="Find"/> returns a blob. The blob is cleared.</summary>
    public static bool Exists(
        string name,
        string? workingDirectory,
        Func<string, byte[]?> readTarget,
        Func<string, byte[]?> readNamed)
    {
        var hit = Find(name, workingDirectory, readTarget, readNamed);
        if (hit is not { } found)
            return false;
        Array.Clear(found.Bytes);
        return true;
    }
}
