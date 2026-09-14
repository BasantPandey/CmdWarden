using CmdWarden.Cli.Harden;
using Microsoft.Win32;

namespace CmdWarden.Tests;

/// <summary>cw doctor --fix-path (#210) on a temp HKCU key that stands in for the machine Environment key.</summary>
public class MachinePathEditorTests
{
    private const string Entry = @"%LOCALAPPDATA%\CmdWarden\shims";

    [Fact]
    public void PrependValue_puts_the_entry_first_once_and_keeps_vendor_entries()
    {
        Assert.Equal(Entry + @";C:\Program Files\Git\cmd;C:\Windows", MachinePathEditor.PrependValue(@"C:\Program Files\Git\cmd;C:\Windows", Entry));
        Assert.Equal(Entry + @";C:\Program Files\Git\cmd", MachinePathEditor.PrependValue(@"C:\Program Files\Git\cmd;" + Entry, Entry));
        Assert.Null(MachinePathEditor.PrependValue(Entry + @";C:\Windows", Entry));
        Assert.Null(MachinePathEditor.PrependValue(Entry.ToLowerInvariant() + @";C:\Windows", Entry));
        Assert.Equal(Entry, MachinePathEditor.PrependValue(null, Entry));
    }

    [Fact]
    public void Prepend_on_a_temp_hive_keeps_REG_EXPAND_SZ_and_is_idempotent()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var name = @"Software\CmdWardenTests\" + Guid.NewGuid().ToString("N");
        using var key = Registry.CurrentUser.CreateSubKey(name);
        try
        {
            key.SetValue("Path", @"C:\Program Files\Git\cmd;%SystemRoot%\system32", RegistryValueKind.ExpandString);

            Assert.True(MachinePathEditor.Prepend(key, Entry));
            Assert.Equal(RegistryValueKind.ExpandString, key.GetValueKind("Path"));
            Assert.Equal(Entry + @";C:\Program Files\Git\cmd;%SystemRoot%\system32",
                key.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames));

            Assert.False(MachinePathEditor.Prepend(key, Entry));
            Assert.Equal(RegistryValueKind.ExpandString, key.GetValueKind("Path"));
            Assert.Equal(Entry + @";C:\Program Files\Git\cmd;%SystemRoot%\system32",
                key.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void PreferredEntry_uses_LOCALAPPDATA_for_the_default_root_and_the_literal_path_elsewhere()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.Equal(Entry, MachinePathEditor.PreferredEntry(Path.Combine(local, "CmdWarden", "shims")));
        Assert.Equal(@"D:\cw\shims", MachinePathEditor.PreferredEntry(@"D:\cw\shims"));
    }

    [Fact]
    public void LogonPath_reads_the_user_environment_block()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var path = MachinePathEditor.LogonPath();
        Assert.False(string.IsNullOrEmpty(path));
        Assert.Contains("system32", path, StringComparison.OrdinalIgnoreCase);
    }
}
