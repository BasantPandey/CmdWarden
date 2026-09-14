using System.Diagnostics;
using CmdWarden.Agent.Approval;
using CmdWarden.Agent.Identity;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>Chain rule, launcher skip list, and run record for the credential helper gate (#202).</summary>
public class HelperChainRuleTests
{
    private const string ShimsDir = @"C:\Users\u\AppData\Local\CmdWarden\shims";
    private const string GitRoot = @"C:\Program Files\Git";
    private const string GitThumb = "AABBCCDDEEFF";
    private static readonly ToolPin DockerPin = new("docker", @"C:\Program Files\Docker\docker.exe", "abc");
    private static readonly ToolPin GitPin = new("git", GitRoot + @"\cmd\git.exe", "def", GitThumb);

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

    private static ProcessNode Helper() => Node(1, ShimsDir + @"\docker-credential-cmdwarden.exe", LauncherKinds.PathHash);
    private static ProcessNode GitHelper() => Node(1, ShimsDir + @"\git-credential-cmdwarden.exe", LauncherKinds.PathHash);
    private static ProcessNode Docker(string kind = LauncherKinds.Authenticode, bool reuse = false) => Node(2, DockerPin.Path, kind, reuse);
    private static ProcessNode Harness() => Node(9, @"C:\Tools\harness.exe");

    private static ProcessNode GitNode(int pid, string relative, string kind = LauncherKinds.Authenticode, string? thumb = GitThumb)
    {
        var node = Node(pid, GitRoot + "\\" + relative, kind);
        node.Thumbprint = thumb;
        return node;
    }

    private static ProcessNode[] VerifiedGitChain(params ProcessNode[] extraBetween) =>
        new[] { GitHelper() }
            .Concat(extraBetween)
            .Concat(new[]
            {
                GitNode(3, @"usr\bin\sh.exe"),
                GitNode(4, @"mingw64\libexec\git-core\git-remote-https.exe"),
                GitNode(5, @"mingw64\libexec\git-core\git.exe"),
                GitNode(6, @"mingw64\bin\git.exe"),
                GitNode(7, @"cmd\git.exe"),
                Node(8, ShimsDir + @"\git.exe", LauncherKinds.PathHash),
                Harness(),
            })
            .ToArray();

    [Fact]
    public void Pinned_docker_at_depth_1_passes()
    {
        Assert.Null(HelperChainRule.Check(new[] { Helper(), Docker(), Harness() }, DockerPin));
    }

    [Theory]
    [InlineData("docker-compose.exe")]
    [InlineData("docker-buildx.exe")]
    public void Plugin_then_pinned_docker_passes(string plugin)
    {
        var chain = new[] { Helper(), Node(5, @"C:\Program Files\Docker\cli-plugins\" + plugin), Docker(), Harness() };
        Assert.Null(HelperChainRule.Check(chain, DockerPin));
    }

    [Fact]
    public void No_docker_above_denies_HelperParentMissing()
    {
        Assert.Equal(PolicyReasonCodes.HelperParentMissing, HelperChainRule.Check(new[] { Helper(), Harness() }, DockerPin));
        Assert.Equal(PolicyReasonCodes.HelperParentMissing, HelperChainRule.Check(new[] { Helper() }, DockerPin));
    }

    [Fact]
    public void Unpinned_docker_denies()
    {
        var other = Node(2, @"C:\Other\docker.exe");
        Assert.Equal(PolicyReasonCodes.HelperParentMissing, HelperChainRule.Check(new[] { Helper(), other, Harness() }, DockerPin));
    }

    [Fact]
    public void Unsigned_or_reused_docker_denies()
    {
        Assert.Equal(PolicyReasonCodes.HelperParentMissing,
            HelperChainRule.Check(new[] { Helper(), Docker(LauncherKinds.PathHash), Harness() }, DockerPin));
        Assert.Equal(PolicyReasonCodes.HelperParentMissing,
            HelperChainRule.Check(new[] { Helper(), Docker(reuse: true), Harness() }, DockerPin));
    }

    [Fact]
    public void Plugin_at_depth_2_without_docker_denies()
    {
        var chain = new[] { Helper(), Node(5, @"C:\Program Files\Docker\cli-plugins\docker-compose.exe"), Harness() };
        Assert.Equal(PolicyReasonCodes.HelperParentMissing, HelperChainRule.Check(chain, DockerPin));
    }

    [Fact]
    public void Git_verified_chain_passes()
    {
        Assert.Null(HelperChainRule.Check(VerifiedGitChain(), GitPin));
    }

    [Fact]
    public void Git_lfs_in_the_span_passes()
    {
        Assert.Null(HelperChainRule.Check(VerifiedGitChain(GitNode(2, @"cmd\git-lfs.exe")), GitPin));
    }

    [Fact]
    public void Git_unsigned_node_in_the_span_denies()
    {
        var chain = VerifiedGitChain(GitNode(2, @"usr\bin\sh.exe", LauncherKinds.PathHash, thumb: null));
        Assert.Equal(PolicyReasonCodes.HelperParentMissing, HelperChainRule.Check(chain, GitPin));
    }

    [Theory]
    [InlineData(@"C:\Program Files\Git\cmd\git.exe")]
    [InlineData(@"C:\Program Files\Git\bin\git.exe")]
    [InlineData(@"C:\Program Files\Git\mingw64\bin\git.exe")]
    [InlineData(@"C:\Program Files\Git\usr\bin\git.exe")]
    public void Git_install_root_is_the_same_for_every_layout(string pin)
    {
        Assert.Equal(@"C:\Program Files\Git", HelperChainRule.InstallRoot(pin));
    }

    [Fact]
    public void Git_install_root_of_a_loose_pin_is_its_own_directory()
    {
        Assert.Equal(@"C:\Tools", HelperChainRule.InstallRoot(@"C:\Tools\git.exe"));
    }

    [Fact]
    public void Git_wrong_signer_in_the_span_denies()
    {
        var chain = VerifiedGitChain(GitNode(2, @"usr\bin\sh.exe", thumb: "OTHER"));
        Assert.Equal(PolicyReasonCodes.HelperParentMissing, HelperChainRule.Check(chain, GitPin));
    }

    [Fact]
    public void Git_no_pinned_git_above_denies()
    {
        var chain = new[]
        {
            GitHelper(),
            GitNode(3, @"usr\bin\sh.exe"),
            Harness(),
        };
        Assert.Equal(PolicyReasonCodes.HelperParentMissing, HelperChainRule.Check(chain, GitPin));
    }

    [Fact]
    public void SelectLauncher_skips_git_signer_walk_nodes_and_the_shim()
    {
        var isProductOwned = LauncherIdentityResolver.IsProductOwned(new[] { GitPin }, ShimsDir);
        var chain = VerifiedGitChain();
        Assert.Same(chain[^1], LauncherIdentityResolver.SelectLauncher(chain, isProductOwned));

        var noShim = new[]
        {
            GitHelper(),
            GitNode(3, @"usr\bin\sh.exe"),
            GitNode(7, @"cmd\git.exe"),
            Harness(),
        };
        Assert.Same(noShim[^1], LauncherIdentityResolver.SelectLauncher(noShim, isProductOwned));
    }

    [Fact]
    public void SelectLauncher_skips_helper_plugin_pinned_tool_and_shim()
    {
        var isProductOwned = LauncherIdentityResolver.IsProductOwned(new[] { DockerPin }, ShimsDir);
        var shim = Node(3, ShimsDir + @"\docker.exe", LauncherKinds.PathHash);
        var plugin = Node(5, @"C:\Program Files\Docker\cli-plugins\docker-compose.exe");
        var terminal = Node(8, @"C:\Windows\System32\WindowsTerminal.exe");

        var chain = new[] { Helper(), plugin, Docker(), shim, Harness(), terminal };
        Assert.Same(chain[4], LauncherIdentityResolver.SelectLauncher(chain, isProductOwned));

        // A pinned tool deeper in the chain does not move the launcher.
        var deep = new[] { Harness(), Docker(), terminal };
        Assert.Same(deep[0], LauncherIdentityResolver.SelectLauncher(deep, isProductOwned));

        // Only product-owned nodes: no launcher, never the pinned tool.
        var onlyOwned = new[] { Helper(), Docker() };
        Assert.Equal(LauncherKinds.Unknown, LauncherIdentityResolver.SelectLauncher(onlyOwned, isProductOwned).Kind);
    }

    [Fact]
    public void SelectLauncher_does_not_skip_by_file_name_alone()
    {
        var isProductOwned = LauncherIdentityResolver.IsProductOwned(new[] { DockerPin }, ShimsDir);
        var terminal = Node(8, @"C:\Windows\System32\WindowsTerminal.exe");

        var renamed = Node(7, @"C:\Tools\docker-buildx.exe");
        Assert.Same(renamed, LauncherIdentityResolver.SelectLauncher(new[] { renamed, terminal }, isProductOwned));

        var fakeHelper = Node(6, @"C:\Tools\docker-credential-cmdwarden.exe");
        Assert.Same(fakeHelper, LauncherIdentityResolver.SelectLauncher(new[] { fakeHelper, terminal }, isProductOwned));
    }

    [Fact]
    public void Run_record_covers_a_live_shim_and_clears_on_exit()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var memory = new ApprovalMemory();
        using var shim = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        })!;
        try
        {
            memory.RecordRun(shim.Id, pidFromPipe: true, "auth:sha1:t", "docker");
            Assert.True(memory.IsRunCovered(new[] { 4, shim.Id, 5 }, "docker"));
            Assert.False(memory.IsRunCovered(new[] { shim.Id }, "git"));
            Assert.False(memory.IsRunCovered(new[] { 4, 5 }, "docker"));

            shim.Kill();
            shim.WaitForExit();
            Assert.False(memory.IsRunCovered(new[] { shim.Id }, "docker"));
        }
        finally
        {
            try { if (!shim.HasExited) shim.Kill(); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Run_record_ignores_a_pid_that_is_not_pipe_bound()
    {
        var memory = new ApprovalMemory();
        memory.RecordRun(Environment.ProcessId, pidFromPipe: false, "auth:sha1:t", "docker");
        Assert.False(memory.IsRunCovered(new[] { Environment.ProcessId }, "docker"));
    }

    [Fact]
    public void Run_record_clears_with_tool_and_launcher_key()
    {
        var memory = new ApprovalMemory();
        var me = new[] { Environment.ProcessId };
        memory.RecordRun(Environment.ProcessId, pidFromPipe: true, "auth:sha1:t", "docker");
        Assert.True(memory.IsRunCovered(me, "docker"));
        Assert.Equal(1, memory.ClearForTool("docker"));
        Assert.False(memory.IsRunCovered(me, "docker"));

        memory.RecordRun(Environment.ProcessId, pidFromPipe: true, "auth:sha1:t", "docker");
        Assert.Equal(1, memory.ClearForLauncherKey("auth:sha1:t"));
        Assert.False(memory.IsRunCovered(me, "docker"));

        memory.RecordRun(Environment.ProcessId, pidFromPipe: true, "auth:sha1:t", "docker");
        Assert.Equal(1, memory.ClearForPolicyChange("auth:sha1:other", "docker"));
        Assert.False(memory.IsRunCovered(me, "docker"));
    }
}
