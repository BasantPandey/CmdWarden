using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Proxy;

namespace CmdWarden.Cli;

/// <summary>
/// #97: <c>cw inject --template FILE</c>, like op inject. The copy of the file with each
/// <c>cw://NAME</c> filled is plain text on disk while the command runs. So it goes in a new folder
/// that only this user can open, and the folder goes away when the run ends. Ctrl+C does not skip
/// that: cw waits for the command to end. A copy that a crash left is deleted on the next run,
/// when the cw process that made it is gone.
/// </summary>
public sealed class TemplateFile : IDisposable
{
    public const string ArgToken = "{template}";
    public const string EnvVar = "CW_TEMPLATE";

    private readonly string _dir;
    private readonly ConsoleCancelEventHandler _waitForChild = (_, e) => e.Cancel = true;

    public string Path { get; }

    private TemplateFile(string dir, string path)
    {
        _dir = dir;
        Path = path;
        Console.CancelKeyPress += _waitForChild;
    }

    /// <summary>The vault names in the template, in order.</summary>
    public static IReadOnlyList<string> Names(string text) => KeyProxy.Placeholders([text]);

    /// <param name="values">Vault values by env name (<see cref="VaultNames.EnvVarName"/>).</param>
    public static TemplateFile Write(string templatePath, string text, IReadOnlyDictionary<string, string> values,
        string? productRoot = null)
    {
        var root = System.IO.Path.Combine(productRoot ?? ProductPaths.Root(), "tmp");
        Directory.CreateDirectory(root);
        SweepLeftovers(root);
        var dir = System.IO.Path.Combine(root, $"{Environment.ProcessId}_{Guid.NewGuid():N}");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(dir).Create(security);
        var path = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(templatePath));
        var filled = KeyProxy.Placeholder().Replace(text, m => values[VaultNames.EnvVarName(m.Groups[1].Value)]);
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(filled));
        return new TemplateFile(dir, path);
    }

    /// <summary>The command arguments with each <see cref="ArgToken"/> replaced by the path of the copy.</summary>
    public IReadOnlyList<string> Arguments(IEnumerable<string> arguments) =>
        arguments.Select(a => a.Replace(ArgToken, Path, StringComparison.Ordinal)).ToList();

    public void Dispose()
    {
        Console.CancelKeyPress -= _waitForChild;
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"{ProductInfo.Name}: could not delete {_dir}: {ex.Message}. Delete it by hand.");
        }
    }

    private static void SweepLeftovers(string root)
    {
        foreach (var dir in new DirectoryInfo(root).EnumerateDirectories())
        {
            if (!int.TryParse(dir.Name.Split('_')[0], out var pid) || IsAlive(pid, dir.CreationTimeUtc))
                continue;
            try
            {
                dir.Delete(recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The command of that run may still hold a file.
            }
        }
    }

    /// <summary>True when the process runs and started before the folder: not a reused id.</summary>
    private static bool IsAlive(int pid, DateTime createdUtc)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return process.StartTime.ToUniversalTime() <= createdUtc;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
