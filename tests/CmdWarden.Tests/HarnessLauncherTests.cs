using CmdWarden.Cli.Launch;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Scan;

namespace CmdWarden.Tests;

/// <summary>#25: cw launch finds the harness, strips token variables, and enrolls the binary.</summary>
public class HarnessLauncherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cw-launch-" + Guid.NewGuid().ToString("N"));

    public HarnessLauncherTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    private string Touch(params string[] parts)
    {
        var path = Path.Combine([_dir, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void Clean_environment_removes_the_scan_variables_and_names_them()
    {
        var env = new Dictionary<string, string>
        {
            ["GH_TOKEN"] = "t1",
            ["github_token"] = "t2",
            ["AZURE_CLIENT_SECRET"] = "t3",
            ["DOCKER_AUTH_CONFIG"] = "t4",
            ["PATH"] = @"C:\bin",
        };
        var (clean, removed) = HarnessLauncher.CleanEnvironment(env);
        Assert.Equal(["PATH"], clean.Keys);
        Assert.Equal(["GH_TOKEN", "GITHUB_TOKEN", "AZURE_CLIENT_SECRET", "DOCKER_AUTH_CONFIG"], removed);
        Assert.All(removed, name => Assert.Contains(name, AmbientEnv.All));
    }

    [Fact]
    public void Native_install_starts_and_enrolls_the_same_binary()
    {
        var exe = Touch("bin", "claude.exe");
        var install = HarnessLauncher.Locate(HarnessLauncher.Find("claude")!, Path.GetDirectoryName(exe), _dir)!;
        Assert.Equal(exe, install.StartPath, ignoreCase: true);
        Assert.Equal(exe, install.ImagePath, ignoreCase: true);
    }

    [Fact]
    public void Npm_install_starts_the_cmd_and_enrolls_the_native_binary()
    {
        var npm = Path.Combine(_dir, "npm");
        Directory.CreateDirectory(npm);
        File.WriteAllText(Path.Combine(npm, "codex.cmd"),
            """
            @ECHO off
            endLocal & "%_prog%"  "%dp0%\node_modules\@openai\codex\bin\codex.js" %*
            """);
        var native = Touch("npm", "node_modules", "@openai", "codex", "node_modules", "@openai", "codex-win32-x64", "vendor", "bin", "codex.exe");
        Touch("npm", "node_modules", "other", "codex.exe");

        var install = HarnessLauncher.Locate(HarnessLauncher.Find("codex")!, npm, _dir)!;
        Assert.EndsWith("codex.cmd", install.StartPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(native, install.ImagePath, ignoreCase: true);
    }

    [Fact]
    public void Gui_harness_starts_from_its_program_folder()
    {
        var exe = Touch("Programs", "cursor", "Cursor.exe");
        var install = HarnessLauncher.Locate(HarnessLauncher.Find("cursor")!, "", _dir)!;
        Assert.Equal(exe, install.StartPath, ignoreCase: true);
        Assert.Null(HarnessLauncher.Locate(HarnessLauncher.Find("claude")!, "", _dir));
    }

    [Fact]
    public void Enroll_adds_an_ai_harness_once_and_keeps_an_existing_enrollment()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var exe = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var store = new PolicyStore(Path.Combine(_dir, "policy.json"));
        store.Load();
        var install = new HarnessInstall(HarnessLauncher.Find("claude")!, exe, [], exe);

        var (key, enrolled) = HarnessLauncher.EnsureEnrolled(install, store);
        Assert.True(enrolled);
        Assert.Equal(LauncherEnrollmentKindNames.AiHarness, store.Launchers[key!].Kind);

        Assert.False(HarnessLauncher.EnsureEnrolled(install, store).Enrolled);
        Assert.Equal((null, false), HarnessLauncher.EnsureEnrolled(install with { ImagePath = null }, store));
    }
}
