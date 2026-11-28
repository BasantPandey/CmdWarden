using System.Diagnostics;
using CmdWarden.Agent;
using CmdWarden.Agent.Approval;
using CmdWarden.Agent.Identity;
using CmdWarden.Cli;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>CLI and gate coverage for reviewed scripts (issue 201).</summary>
public class ScriptCommandTests
{
    [Fact]
    public void Bless_list_and_drop_round_trip()
    {
        var dir = TempDir();
        var script = Write(dir, "release.ps1", "gh release create v1\r\n");
        UsingStore(dir, () =>
        {
            var bless = Capture(() => ScriptCommands.Run(["bless", script, "--cap", "GH=write", "--cap", "aws_cli=read"]));
            Assert.Equal(0, bless.Code);
            Assert.Contains(script, bless.Out, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("gh=write", bless.Out, StringComparison.OrdinalIgnoreCase);

            var list = Capture(() => ScriptCommands.Run(["LiSt"]));
            Assert.Equal(0, list.Code);
            Assert.Contains(script, list.Out, StringComparison.OrdinalIgnoreCase);

            var again = Capture(() => ScriptCommands.Run(["bless", script, "--cap", "gh-tool=write"]));
            Assert.Equal(0, again.Code);
            Assert.Contains("gh-tool=write", again.Out, StringComparison.Ordinal);

            var drop = Capture(() => ScriptCommands.Run(["drop", script]));
            Assert.Equal(0, drop.Code);
            Assert.Contains("Dropped", drop.Out, StringComparison.Ordinal);

            var empty = Capture(() => ScriptCommands.Run(["list"]));
            Assert.Equal(0, empty.Code);
            Assert.Contains("No reviewed scripts.", empty.Out, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Help_and_bad_args_do_not_save_a_review()
    {
        var dir = TempDir();
        var script = Write(dir, "release.ps1", "gh release create v1\r\n");
        var missing = Path.Combine(dir, "missing.ps1");
        UsingStore(dir, () =>
        {
            Assert.Equal(1, Capture(() => ScriptCommands.Run([])).Code);
            Assert.Equal(1, Capture(() => ScriptCommands.Run(["nope"])).Code);
            Assert.Equal(1, Capture(() => ScriptCommands.Run(["bless"])).Code);
            Assert.Equal(1, Capture(() => ScriptCommands.Run(["bless", script])).Code);
            Assert.Equal(1, Capture(() => ScriptCommands.Run(["bless", "--cap", "gh=write"])).Code);
            Assert.Equal(1, Capture(() => ScriptCommands.Run(["bless", script, script, "--cap", "gh=write"])).Code);
            Assert.Equal(1, Capture(() => ScriptCommands.Run(["bless", script, "--cap"])).Code);
            Assert.Equal(1, Capture(() => ScriptCommands.Run(["bless", script, "--weird"])).Code);
            Assert.Equal(1, Capture(() => ScriptCommands.Run(["bless", script, "--cap", "gh"])).Code);
            Assert.Equal(1, Capture(() => ScriptCommands.Run(["bless", script, "--cap", "=write"])).Code);
            Assert.Equal(1, Capture(() => ScriptCommands.Run(["bless", script, "--cap", "gh.cli=write"])).Code);
            Assert.Equal(1, Capture(() => ScriptCommands.Run(["bless", script, "--cap", "gh=full"])).Code);
            Assert.Equal(1, Capture(() => ScriptCommands.Run(["bless", missing, "--cap", "gh=write"])).Code);
            Assert.Equal(1, Capture(() => ScriptCommands.Run(["drop"])).Code);
            Assert.Equal(1, Capture(() => ScriptCommands.Run(["drop", script, script])).Code);
            var absent = Capture(() => ScriptCommands.Run(["drop", missing]));
            Assert.Equal(1, absent.Code);
            Assert.Contains("No reviewed script", absent.Out, StringComparison.Ordinal);
            Assert.Empty(new BlessedScriptStore(Path.Combine(dir, BlessedScriptStore.FileName)).Load());
        });
    }

    [Fact]
    public void A_store_error_returns_one()
    {
        var dir = TempDir();
        var script = Write(dir, "release.ps1", "x\r\n");
        foreach (var error in new Exception[]
        {
            new IOException("disk"),
            new UnauthorizedAccessException("acl"),
            new ArgumentException("bad"),
        })
        {
            var previous = ScriptCommands.StorePath;
            ScriptCommands.StorePath = () => throw error;
            try
            {
                var result = Capture(() => ScriptCommands.Run(["bless", script, "--cap", "gh=write"]));
                Assert.Equal(1, result.Code);
                Assert.Contains(error.Message, result.Err, StringComparison.Ordinal);
            }
            finally
            {
                ScriptCommands.StorePath = previous;
            }
        }
    }

    [Fact]
    public void List_prints_a_row_whose_caps_are_null()
    {
        var dir = TempDir();
        var file = Path.Combine(dir, BlessedScriptStore.FileName);
        File.WriteAllText(file, """{"scripts":[{"path":"C:\\src\\release.ps1","sha256":"abc","caps":null}]}""");
        UsingStore(dir, () =>
        {
            var list = Capture(() => ScriptCommands.Run(["list"]));
            Assert.Equal(0, list.Code);
            Assert.Contains(@"C:\src\release.ps1", list.Out, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_script_command_reaches_the_cli()
    {
        var dir = TempDir();
        UsingStore(dir, () =>
        {
            var list = CaptureAsync(() => CliApp.RunAsync(["script", "list"])).GetAwaiter().GetResult();
            Assert.Equal(0, list.Code);
            Assert.Contains("No reviewed scripts.", list.Out, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_gate_allows_a_live_reviewed_script()
    {
        var dir = TempDir();
        var script = Write(dir, "release.ps1", "Start-Sleep -Seconds 60\r\n");
        var store = new BlessedScriptStore(Path.Combine(dir, BlessedScriptStore.FileName));
        store.Save(new ScriptBlessing
        {
            Path = script,
            Sha256 = BoundFiles.Sha256(script),
            Caps = new Dictionary<string, string> { ["gh"] = "write" },
        });

        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var psi = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(script);
        using var process = Process.Start(psi);
        Assert.NotNull(process);
        try
        {
            var service = Service(dir);
            var selected = new ProcessNode { Pid = process.Id, ParentPid = 0, FileName = null };
            var launcher = LauncherOf(
                process.Id,
                selected,
                new ProcessNode { Pid = 0, ParentPid = 0, FileName = "gone.exe" },
                selected);

            Assert.False(service.ReviewedScriptAllows(EmptyLauncher(), "gh", CommandClass.Write));
            Assert.True(service.ReviewedScriptAllows(launcher, "gh", CommandClass.Write));
            Assert.False(service.ReviewedScriptAllows(launcher, "gh", CommandClass.SecretReveal));

            var named = new ProcessNode { Pid = process.Id, ParentPid = 0, FileName = "powershell.exe" };
            Assert.True(service.ReviewedScriptAllows(LauncherOf(process.Id, named, named), "gh", CommandClass.Read));
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }

        Assert.Null(SessionAgentService.HashOrNull(Path.Combine(dir, "missing.ps1")));
        Assert.Null(SessionAgentService.HashOrNull(dir));

        var lockedPath = Path.Combine(dir, BlessedScriptStore.FileName);
        using (new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => new BlessedScriptStore(lockedPath).Load());
            Assert.False(Service(dir).ReviewedScriptAllows(EmptyLauncher(), "gh", CommandClass.Write));
        }
    }

    private static SessionAgentService Service(string root)
    {
        var pins = new ToolPinStore(root);
        return new SessionAgentService(
            new AgentRuntimeInfo("pipe", Path.Combine(root, "policy.json"), root),
            new CredentialVault(),
            new LauncherIdentityResolver(pins),
            new PolicyStore(Path.Combine(root, "policy.json")),
            new SilentGate(),
            new ApprovalMemory(),
            pins,
            new AuditLog(root),
            new AlarmNotifier(false));
    }

    private static LauncherResolution EmptyLauncher() => LauncherOf(1, new ProcessNode { Pid = 1, ParentPid = 0 });

    private static LauncherResolution LauncherOf(int pid, ProcessNode selected, params ProcessNode[] chain) => new()
    {
        ClientPid = pid,
        ClientPidFromPipe = true,
        Selected = selected,
        Chain = chain,
        AutoApproveEligible = false,
    };

    private sealed class SilentGate : IApprovalGate
    {
        public ApprovalAnswer Prompt(ApprovalRequest request) =>
            throw new InvalidOperationException("the test must not ask");
    }

    private static void UsingStore(string dir, Action body)
    {
        var previous = ScriptCommands.StorePath;
        ScriptCommands.StorePath = () => Path.Combine(dir, BlessedScriptStore.FileName);
        try
        {
            body();
        }
        finally
        {
            ScriptCommands.StorePath = previous;
        }
    }

    private static (int Code, string Out, string Err) Capture(Func<int> run) =>
        CaptureAsync(() => Task.FromResult(run())).GetAwaiter().GetResult();

    private static Task<(int Code, string Out, string Err)> CaptureAsync(Func<Task<int>> run) =>
        CliOutput.Capture(run);

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cw-script-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Write(string dir, string name, string text)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, text);
        return path;
    }
}
