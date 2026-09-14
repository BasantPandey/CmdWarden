using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CmdWarden.Cli.Harden;

/// <summary>
/// cw doctor --fix-path (#210): prepend the shims dir to the machine PATH as REG_EXPAND_SZ through
/// one elevated run of cw itself. <see cref="LogonPath"/> builds the PATH a new logon gets, so the
/// caller can see whether a <c>%LOCALAPPDATA%</c> entry expands per user or needs the literal path.
/// </summary>
[SupportedOSPlatform("windows")]
public static class MachinePathEditor
{
    public const string MachineEnvironmentKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    public const string ElevatedFlag = "--elevated";

    /// <summary>The entry to write: <c>%LOCALAPPDATA%\CmdWarden\shims</c> when the product root is the default.</summary>
    public static string PreferredEntry(string shimsDir)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var full = Path.GetFullPath(shimsDir);
        return local.Length > 0 && full.StartsWith(local + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? "%LOCALAPPDATA%" + full[local.Length..]
            : full;
    }

    /// <summary>New PATH value with <paramref name="entry"/> first, or null when it already is. Other entries keep their order.</summary>
    public static string? PrependValue(string? current, string entry)
    {
        var parts = (current ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (parts.Count > 0 && string.Equals(parts[0], entry, StringComparison.OrdinalIgnoreCase))
            return null;
        parts.RemoveAll(p => string.Equals(p, entry, StringComparison.OrdinalIgnoreCase));
        parts.Insert(0, entry);
        return string.Join(';', parts);
    }

    /// <summary>Prepend on the Path value of <paramref name="key"/>; the kind stays REG_EXPAND_SZ. True when written.</summary>
    public static bool Prepend(RegistryKey key, string entry)
    {
        var current = key.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        if (PrependValue(current, entry) is not { } updated)
            return false;
        key.SetValue("Path", updated, RegistryValueKind.ExpandString);
        return true;
    }

    /// <summary>Runs elevated: write the machine PATH and tell open shells the environment changed.</summary>
    public static bool PrependMachine(string entry)
    {
        using var key = Registry.LocalMachine.OpenSubKey(MachineEnvironmentKey, writable: true)
            ?? throw new InvalidOperationException("machine Environment key not found");
        var changed = Prepend(key, entry);
        if (changed)
            SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "Environment", SMTO_ABORTIFHUNG, 5000, out _);
        return changed;
    }

    /// <summary>One UAC prompt: cw doctor --fix-path --elevated &lt;entry&gt;. Returns the exit code; null when the prompt was refused.</summary>
    public static int? RunElevated(string entry)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("cannot find the cw executable");
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (var a in new[] { "doctor", "--fix-path", ElevatedFlag, entry })
            psi.ArgumentList.Add(a);
        try
        {
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("elevated cw did not start");
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return null;
        }
    }

    /// <summary>The PATH a new logon of this user gets (userenv builds it the same way). Null when unavailable.</summary>
    public static string? LogonPath()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        if (!CreateEnvironmentBlock(out var block, identity.Token, false))
            return null;
        try
        {
            var p = block;
            while (true)
            {
                var line = Marshal.PtrToStringUni(p);
                if (string.IsNullOrEmpty(line))
                    return null;
                if (line.StartsWith("Path=", StringComparison.OrdinalIgnoreCase))
                    return line[5..];
                p += (line.Length + 1) * 2;
            }
        }
        finally
        {
            DestroyEnvironmentBlock(block);
        }
    }

    public static bool IsFirstAtLogon(string shimsDir)
    {
        var first = (LogonPath() ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return first is not null && string.Equals(Norm(first), Norm(shimsDir), StringComparison.OrdinalIgnoreCase);
    }

    private static string Norm(string p)
    {
        try { return Path.GetFullPath(p).TrimEnd('\\', '/'); }
        catch { return p; }
    }

    private const int ErrorCancelled = 1223;
    private static readonly IntPtr HWND_BROADCAST = new(0xffff);
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);
}
