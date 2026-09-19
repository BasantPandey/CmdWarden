using CmdWarden.Agent.Identity;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>An enrolled AI harness above a shell wins the launcher selection.</summary>
public class HarnessAncestorTests
{
    private static ProcessNode Node(int pid, string path, string kind = LauncherKinds.Authenticode, bool reuse = false) => new()
    {
        Pid = pid,
        ParentPid = pid + 1,
        Path = path,
        FileName = Path.GetFileName(path),
        Kind = kind,
        PolicyKey = kind == LauncherKinds.Unknown ? LauncherKinds.PolicyKeyUnknown : kind + ":" + pid,
        PidReuseSuspected = reuse,
    };

    private static LauncherResolution Resolution(ProcessNode selected, params ProcessNode[] chain) => new()
    {
        ClientPid = chain[0].Pid,
        ClientPidFromPipe = true,
        Selected = selected,
        Chain = chain,
        AutoApproveEligible = true,
    };

    [Fact]
    public void Harness_above_shell_wins()
    {
        var cw = Node(1, @"C:\cw\cw.exe", LauncherKinds.PathHash);
        var bash = Node(2, @"C:\Git\bash.exe");
        var claude = Node(3, @"C:\claude\claude.exe");
        var pwsh = Node(4, @"C:\pwsh\pwsh.exe");
        var r = LauncherIdentityResolver.PreferHarnessAncestor(
            Resolution(bash, cw, bash, claude, pwsh), key => key == claude.PolicyKey);

        Assert.Same(claude, r.Selected);
        Assert.True(r.AutoApproveEligible);
        Assert.Contains("ai-harness ancestor", r.Notes, StringComparison.Ordinal);
    }

    [Fact]
    public void No_harness_keeps_selection()
    {
        var bash = Node(2, @"C:\Git\bash.exe");
        var pwsh = Node(4, @"C:\pwsh\pwsh.exe");
        var r = LauncherIdentityResolver.PreferHarnessAncestor(Resolution(bash, bash, pwsh), _ => false);
        Assert.Same(bash, r.Selected);
    }

    [Fact]
    public void Pid_reuse_or_unknown_harness_node_is_skipped()
    {
        var bash = Node(2, @"C:\Git\bash.exe");
        var stale = Node(3, @"C:\claude\claude.exe", reuse: true);
        var unknown = Node(5, @"C:\x\x.exe", LauncherKinds.Unknown);
        var r = LauncherIdentityResolver.PreferHarnessAncestor(
            Resolution(bash, bash, stale, unknown), key => key == stale.PolicyKey || key == unknown.PolicyKey);
        Assert.Same(bash, r.Selected);
    }
}
