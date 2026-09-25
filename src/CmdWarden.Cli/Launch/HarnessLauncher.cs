using System.Diagnostics;
using System.Runtime.Versioning;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Scan;

namespace CmdWarden.Cli.Launch;

/// <summary>
/// One AI harness <c>cw launch</c> knows (#25). <paramref name="Command"/> is the name on PATH;
/// <paramref name="Image"/> is the binary that runs the agent loop and starts shells, which is the
/// launcher CmdWarden sees and enrolls.
/// </summary>
public sealed record Harness(string Id, string DisplayName, string Command, string Image, bool Gui);

/// <summary>Where a harness lives on this PC: what to start, and which binary to enroll.</summary>
public sealed record HarnessInstall(Harness Harness, string StartPath, IReadOnlyList<string> StartArgs, string? ImagePath);

/// <summary>
/// <c>cw launch &lt;harness&gt;</c> (#25): start Claude Code, Codex, or Cursor without the token
/// variables the scan detectors know, and enroll the harness binary as an AI harness when needed.
/// </summary>
[SupportedOSPlatform("windows")]
public static class HarnessLauncher
{
    public static readonly IReadOnlyList<Harness> Catalog =
    [
        new("claude", "Claude Code", "claude", "claude.exe", Gui: false),
        new("codex", "Codex", "codex", "codex.exe", Gui: false),
        new("cursor", "Cursor", "cursor", "Cursor.exe", Gui: true),
    ];

    public static Harness? Find(string id) =>
        Catalog.FirstOrDefault(h => string.Equals(h.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Finds the harness. An npm install puts a .cmd on PATH; the image is then the native binary
    /// under its node_modules folder. Cursor's .cmd runs its CLI, so Cursor starts from Cursor.exe.
    /// </summary>
    public static HarnessInstall? Locate(Harness harness, string? pathEnv = null, string? localAppData = null)
    {
        var onPath = InjectRunner.ResolveProgram(harness.Command, pathEnv);
        if (harness.Gui)
        {
            var programs = Path.Combine(localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", harness.Id);
            var candidates = new List<string> { Path.Combine(programs, harness.Image) };
            if (Path.IsPathRooted(onPath))
                candidates.Add(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(onPath)!, "..", "..", "..", harness.Image)));
            var exe = candidates.FirstOrDefault(File.Exists);
            return exe is null ? null : new HarnessInstall(harness, exe, [], exe);
        }

        if (!Path.IsPathRooted(onPath))
            return null;
        if (Path.GetFileName(onPath).Equals(harness.Image, StringComparison.OrdinalIgnoreCase))
            return new HarnessInstall(harness, onPath, [], onPath);

        // npm shim: cmd runs node, node runs the native binary. That binary starts the shells.
        var package = NpmPackageDir(onPath);
        var image = package is not null && Directory.Exists(package)
            ? Directory.EnumerateFiles(package, harness.Image, SearchOption.AllDirectories).FirstOrDefault()
            : null;
        return new HarnessInstall(harness, onPath, [], image);
    }

    /// <summary>The package folder an npm .cmd shim runs, from its <c>node_modules\[@scope\]name\</c> path.</summary>
    public static string? NpmPackageDir(string cmdPath)
    {
        string text;
        try
        {
            text = File.ReadAllText(cmdPath);
        }
        catch (IOException)
        {
            return null;
        }
        var match = System.Text.RegularExpressions.Regex.Match(text, @"node_modules\\((?:@[^\\""]+\\)?[^\\""]+)\\");
        return match.Success ? Path.Combine(Path.GetDirectoryName(cmdPath)!, "node_modules", match.Groups[1].Value) : null;
    }

    /// <summary>A copy of <paramref name="environment"/> without the ambient credential variables, and the names removed.</summary>
    public static (Dictionary<string, string> Clean, IReadOnlyList<string> Removed) CleanEnvironment(IReadOnlyDictionary<string, string> environment)
    {
        var clean = new Dictionary<string, string>(environment, StringComparer.OrdinalIgnoreCase);
        var removed = AmbientEnv.All.Where(name => clean.Remove(name)).ToList();
        return (clean, removed);
    }

    /// <summary>
    /// Enrolls the harness binary as an AI harness when its key has no enrollment yet. An existing
    /// enrollment, of any kind, stays. Returns the key, or null when the binary is unknown.
    /// </summary>
    public static (string? Key, bool Enrolled) EnsureEnrolled(HarnessInstall install, PolicyStore store)
    {
        if (install.ImagePath is null)
            return (null, false);
        var id = LauncherImage.Identify(install.ImagePath);
        if (id.PolicyKey == LauncherKinds.PolicyKeyUnknown)
            return (null, false);
        if (store.Launchers.ContainsKey(id.PolicyKey))
            return (id.PolicyKey, false);
        store.Enroll(id.PolicyKey, LauncherEnrollmentKind.AiHarness, install.ImagePath);
        store.Save();
        return (id.PolicyKey, true);
    }

    /// <summary>Start the harness with the clean environment. A console harness shares this console, and cw waits for it.</summary>
    public static int Start(HarnessInstall install, IReadOnlyDictionary<string, string> environment, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(install.StartPath) { UseShellExecute = false };
        foreach (var a in install.StartArgs.Concat(args))
            psi.ArgumentList.Add(a);
        psi.Environment.Clear();
        foreach (var (key, value) in environment)
            psi.Environment[key] = value;
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {install.StartPath}.");
        if (install.Harness.Gui)
            return 0;
        process.WaitForExit();
        return process.ExitCode;
    }

    /// <summary>A GUI harness that already runs gets the new window in the old process, with the old environment.</summary>
    public static bool IsRunning(Harness harness) =>
        Process.GetProcessesByName(Path.GetFileNameWithoutExtension(harness.Image)).Length > 0;

    public static Dictionary<string, string> CurrentEnvironment()
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
            env[e.Key.ToString()!] = e.Value?.ToString() ?? "";
        return env;
    }
}
