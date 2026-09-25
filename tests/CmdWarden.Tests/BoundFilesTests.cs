using CmdWarden.Agent.Approval;
using CmdWarden.Cli;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>#30: an approval binds the binary and each script the command runs.</summary>
public class BoundFilesTests
{
    [Theory]
    [InlineData("bash", "deploy.sh", "deploy.sh")]
    [InlineData("bash", "-e deploy.sh", "deploy.sh")]
    [InlineData("bash", "-c echo", null)]
    [InlineData(@"C:\Program Files\PowerShell\7\pwsh.exe", "-NoProfile -File x.ps1 a", "x.ps1")]
    [InlineData("pwsh", "x.ps1", "x.ps1")]
    [InlineData("powershell", "-Command Get-Date", null)]
    [InlineData("pwsh", "-EncodedCommand ZQBjAGgAbwA=", null)]
    [InlineData("python", "-m http.server", null)]
    [InlineData("python3", "-u tool.py --flag", "tool.py")]
    [InlineData("node", "index.js", "index.js")]
    [InlineData("node", "-e console.log(1)", null)]
    [InlineData("cmd", "/c build.cmd", "build.cmd")]
    [InlineData("cmd", "/c echo hi", null)]
    [InlineData("gh", "pr list", null)]
    public void ScriptArgument_finds_the_script_an_interpreter_runs(string program, string args, string? expected) =>
        Assert.Equal(expected, BoundFiles.ScriptArgument(program, args.Split(' ')));

    [Fact]
    public void Find_lock_and_mismatch()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cw-bound-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var script = Path.Combine(dir, "deploy.sh");
            File.WriteAllText(script, "echo one");
            var bash = Path.Combine(Environment.SystemDirectory, "cmd.exe");

            var found = BoundFiles.Find(bash, ["/c", "deploy.cmd"], dir);
            Assert.Equal([bash], found, StringComparer.OrdinalIgnoreCase);
            found = BoundFiles.Find("bash", ["deploy.sh"], dir);
            Assert.Equal([script], found);

            var approved = BoundFiles.Hash(found);
            using (BoundFiles.Lock(found))
            {
                Assert.ThrowsAny<IOException>(() => File.WriteAllText(script, "echo two"));
                Assert.ThrowsAny<IOException>(() => File.Delete(script));
                Assert.Empty(BoundFiles.Mismatches(approved));
            }

            File.WriteAllText(script, "echo two");
            Assert.Equal([script], BoundFiles.Mismatches(approved));
            Assert.Equal([script], BoundFiles.Changed(approved, BoundFiles.Hash(found)));
            Assert.Null(BoundFiles.LockVerified(script, approved[0].Sha256));
            using var ok = BoundFiles.LockVerified(script, BoundFiles.Sha256(script));
            Assert.NotNull(ok);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ResolveProgram_uses_PATH_and_PATHEXT()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cw-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var tool = Path.Combine(dir, "mytool.cmd");
            File.WriteAllText(tool, "@echo off");
            Assert.Equal(tool, InjectRunner.ResolveProgram("mytool", dir, ".EXE;.CMD"));
            Assert.Equal(tool, InjectRunner.ResolveProgram("mytool.cmd", dir, ".EXE"));
            Assert.Equal("missing", InjectRunner.ResolveProgram("missing", dir, ".EXE"));
            Assert.Equal(tool, InjectRunner.ResolveProgram(tool));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Session_grant_covers_only_the_approved_files_and_hashes()
    {
        var memory = new ApprovalMemory();
        var pid = Environment.ProcessId;
        BoundFile[] v1 = [new(@"C:\x\deploy.sh", "aa")];
        BoundFile[] v2 = [new(@"C:\x\deploy.sh", "bb")];
        BoundFile[] other = [new(@"C:\x\other.sh", "cc")];

        Assert.NotNull(memory.Grant(pid, null, "key", "terminal", "inject", "T", CommandClass.Write, files: v1));
        Assert.NotNull(memory.TryUseSession(pid, "inject", "T", CommandClass.Write, v1));
        Assert.NotNull(memory.TryUseSession(pid, "inject", "T", CommandClass.Write, []));
        Assert.Null(memory.TryUseSession(pid, "inject", "T", CommandClass.Write, v2));
        Assert.Null(memory.TryUseSession(pid, "inject", "T", CommandClass.Write, other));

        // A later approve of the new content replaces the old hash.
        Assert.NotNull(memory.Grant(pid, null, "key", "terminal", "inject", "T", CommandClass.Write, files: v2));
        Assert.NotNull(memory.TryUseSession(pid, "inject", "T", CommandClass.Write, v2));
        Assert.Null(memory.TryUseSession(pid, "inject", "T", CommandClass.Write, v1));
    }
}
