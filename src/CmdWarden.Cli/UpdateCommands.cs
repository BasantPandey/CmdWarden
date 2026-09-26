using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.Versioning;
using CmdWarden.Contracts;
using Microsoft.Win32;

namespace CmdWarden.Cli;

/// <summary>
/// cw update and cw uninstall (#45). Both start a script in a new window and exit, because the
/// script stops every cw process and replaces the files of this one.
/// </summary>
public static class UpdateCommands
{
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CmdWarden";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public static async Task<int> UpdateAsync(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help"))
        {
            Console.WriteLine("Usage: cw update [--check]");
            Console.WriteLine("  Download the setup zip of the newest release, check its sha256, and run its installer.");
            Console.WriteLine("  --check  Only say if a newer release is available.");
            return 0;
        }
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("cw update runs on Windows only.");
            return 1;
        }
        var current = ProductInfo.Version;
        LatestRelease latest;
        try
        {
            latest = await ReleaseUpdate.GetLatestAsync(Http).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                                   or InvalidDataException or KeyNotFoundException)
        {
            Console.Error.WriteLine($"cw update: cannot read the newest release: {ex.Message}");
            return 1;
        }
        Ui.Kv("installed", current);
        Ui.Kv("newest", latest.Version);
        if (!ReleaseUpdate.IsNewer(latest.Version, current))
        {
            Console.WriteLine($"{ProductInfo.Name} {current} is up to date.");
            return 0;
        }
        if (args.Contains("--check"))
        {
            Console.WriteLine($"{ProductInfo.Name} {latest.Version} is available. Run cw update.");
            return 0;
        }
        if (RegisteredZipInstall() is { } zipDir)
        {
            Console.Error.WriteLine($"cw update installs the dotnet tool. This copy is a zip install in {zipDir}.");
            Console.Error.WriteLine($"Download CmdWarden.{latest.Version}-win-x64.zip from https://github.com/{ReleaseUpdate.Repo}/releases/latest.");
            return 1;
        }
        if (latest.SetupZip is not { } asset)
        {
            Console.Error.WriteLine($"cw update: the release {latest.Tag} has no {latest.SetupZipName}.");
            return 1;
        }

        var work = Path.Combine(Path.GetTempPath(), "CmdWarden-update-" + Guid.NewGuid().ToString("N"));
        string installer;
        try
        {
            var zip = await ReleaseUpdate.DownloadVerifiedAsync(Http, asset, work).ConfigureAwait(false);
            Ui.Kv("sha256", asset.Sha256 + " (ok)");
            var setup = Path.Combine(work, "setup");
            ZipFile.ExtractToDirectory(zip, setup);
            installer = Path.Combine(setup, "Install-CmdWarden.ps1");
            if (!File.Exists(installer))
                throw new InvalidDataException($"{asset.Name} has no Install-CmdWarden.ps1.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or IOException)
        {
            Console.Error.WriteLine($"cw update: {ex.Message}");
            TryDelete(work);
            return 1;
        }

        StartInWindow(PowerShellExe(), $"-NoProfile -ExecutionPolicy Bypass -File \"{installer}\" -Force -Yes");
        Console.WriteLine($"The installer for {ProductInfo.Name} {latest.Version} runs in a new window. cw stops now.");
        return 0;
    }

    public static int Uninstall(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help"))
        {
            Console.WriteLine("Usage: cw uninstall");
            Console.WriteLine("  Run the uninstaller that Settings > Apps > CmdWarden > Uninstall runs.");
            return 0;
        }
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("cw uninstall runs on Windows only.");
            return 1;
        }
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
        if (key?.GetValue("UninstallString") is not string command || SplitCommand(command) is not var (exe, arguments))
        {
            Console.Error.WriteLine("CmdWarden has no entry in Settings > Apps.");
            Console.Error.WriteLine("Run uninstall.cmd from the setup zip, or scripts\\Uninstall-CmdWarden.ps1.");
            return 1;
        }
        StartInWindow(exe, arguments);
        Console.WriteLine("The uninstaller runs in a new window. cw stops now.");
        return 0;
    }

    /// <summary>
    /// A registry command line to the program and its arguments: "C:\a b\x.exe" -f "y" gives
    /// (C:\a b\x.exe, -f "y"). Null when the line is empty or a quote does not close.
    /// </summary>
    public static (string Exe, string Arguments)? SplitCommand(string command)
    {
        var line = command.Trim();
        if (line.Length == 0)
            return null;
        if (line[0] == '"')
        {
            var close = line.IndexOf('"', 1);
            return close < 0 ? null : (line[1..close], line[(close + 1)..].Trim());
        }
        var space = line.IndexOf(' ');
        return space < 0 ? (line, "") : (line[..space], line[(space + 1)..].Trim());
    }

    /// <summary>The folder of a zip install that is this copy of cw, or null for a dotnet tool install.</summary>
    [SupportedOSPlatform("windows")]
    private static string? RegisteredZipInstall()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
        if (key?.GetValue("InstallLocation") is not string location || location.Length == 0)
            return null;
        var dir = Path.GetFullPath(location).TrimEnd('\\');
        var self = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\');
        return !dir.EndsWith(@"\.store\cmdwarden", StringComparison.OrdinalIgnoreCase)
            && self.StartsWith(dir, StringComparison.OrdinalIgnoreCase) ? dir : null;
    }

    private static string PowerShellExe() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");

    private static void StartInWindow(string exe, string arguments) =>
        Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = true })?.Dispose();

    private static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temp folder; Windows cleans it later.
        }
    }
}
