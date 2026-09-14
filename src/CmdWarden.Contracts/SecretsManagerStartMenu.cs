using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CmdWarden.Contracts;

/// <summary>
/// Per-user Start Menu and Desktop shortcuts for CmdWarden Vault (ticket #97).
/// </summary>
[SupportedOSPlatform("windows")]
public static class SecretsManagerStartMenu
{
    public const string ShortcutFileName = "CmdWarden Vault.lnk";

    /// <summary>User Start Menu Programs folder + shortcut name.</summary>
    public static string ShortcutPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            ShortcutFileName);

    public static bool ShortcutExists() => File.Exists(ShortcutPath);

    /// <summary>User Desktop folder + shortcut name.</summary>
    public static string DesktopShortcutPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            ShortcutFileName);

    public static bool DesktopShortcutExists() => File.Exists(DesktopShortcutPath);

    /// <summary>
    /// Create or overwrite the Start Menu shortcut pointing at <paramref name="targetExe"/>.
    /// Returns the .lnk path.
    /// </summary>
    public static string Install(string targetExe) => Write(targetExe, ShortcutPath);

    /// <summary>Create or overwrite the Desktop shortcut. Returns the .lnk path.</summary>
    public static string InstallDesktop(string targetExe) => Write(targetExe, DesktopShortcutPath);

    private static string Write(string targetExe, string lnk)
    {
        if (string.IsNullOrWhiteSpace(targetExe) || !File.Exists(targetExe))
            throw new FileNotFoundException("Secrets manager exe not found.", targetExe);

        Directory.CreateDirectory(Path.GetDirectoryName(lnk)!);

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell COM progressive ID is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Could not create WScript.Shell.");
        try
        {
            dynamic shortcut = shell.CreateShortcut(lnk);
            shortcut.TargetPath = Path.GetFullPath(targetExe);
            shortcut.WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(targetExe))!;
            shortcut.Description = "CmdWarden Vault - list, add, and delete secret names";
            shortcut.Save();
        }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell))
                Marshal.FinalReleaseComObject(shell);
        }

        return lnk;
    }

    /// <summary>Remove the Start Menu shortcut if present. Returns true if a file was deleted.</summary>
    public static bool Remove() => Delete(ShortcutPath);

    /// <summary>Remove the Desktop shortcut if present. Returns true if a file was deleted.</summary>
    public static bool RemoveDesktop() => Delete(DesktopShortcutPath);

    private static bool Delete(string lnk)
    {
        if (!File.Exists(lnk))
            return false;
        File.Delete(lnk);
        return true;
    }
}
