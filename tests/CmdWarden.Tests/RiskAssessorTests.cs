using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>A git working folder made from files only: HEAD, config, and the remote default branch.</summary>
internal sealed class FakeGitRepo : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "cw-repo-" + Guid.NewGuid().ToString("N"));

    public FakeGitRepo(string branch = "feature-x", string url = "https://github.com/owner/repo.git", string? remoteHead = "main")
    {
        var git = Path.Combine(Dir, ".git");
        Directory.CreateDirectory(Path.Combine(git, "refs", "remotes", "origin"));
        File.WriteAllText(Path.Combine(git, "HEAD"), $"ref: refs/heads/{branch}\n");
        File.WriteAllText(Path.Combine(git, "config"),
            $"[core]\n\tbare = false\n[remote \"origin\"]\n\turl = {url}\n\tfetch = +refs/heads/*:refs/remotes/origin/*\n" +
            $"[branch \"{branch}\"]\n\tremote = origin\n\tmerge = refs/heads/{branch}\n");
        if (remoteHead is not null)
            File.WriteAllText(Path.Combine(git, "refs", "remotes", "origin", "HEAD"), $"ref: refs/remotes/origin/{remoteHead}\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(Dir, recursive: true); } catch { /* ignore */ }
    }
}

/// <summary>#35 risk signals and impact lines.</summary>
public class RiskAssessorTests
{
    private static RiskAssessment Git(string cwd, params string[] argv) =>
        RiskAssessor.Assess("git", argv, CommandClass.Write, cwd);

    [Fact]
    public void Push_to_a_feature_branch_is_low_risk()
    {
        using var repo = new FakeGitRepo();
        Assert.Equal(new RiskAssessment(RiskLevel.Low, "Pushes to feature-x of owner/repo. Not the default branch (main)."), Git(repo.Dir, "push"));
        Assert.Equal(RiskLevel.Low, Git(repo.Dir, "push", "-u", "origin", "feature-x").Level);
        Assert.Equal(RiskLevel.Low, Git(repo.Dir, "push", "origin", "HEAD:refs/heads/other").Level);
    }

    [Theory]
    [InlineData("Force-pushes to main of owner/repo. You cannot undo this.", "push", "--force", "origin", "main")]
    [InlineData("Force-pushes to main of owner/repo. You cannot undo this.", "push", "origin", "+main")]
    [InlineData("Force-pushes to main of owner/repo. You cannot undo this.", "push", "--force-with-lease", "origin", "feature-x:main")]
    [InlineData("Force-pushes to main of owner/repo. You cannot undo this.", "push", "-uf", "origin", "main")]
    [InlineData("Deletes main of owner/repo. You cannot undo this.", "push", "origin", ":main")]
    [InlineData("Mirrors every ref to owner/repo, and deletes remote branches that are not local. You cannot undo this.", "push", "--mirror")]
    public void Force_and_delete_on_the_default_branch_are_high_risk(string impact, params string[] argv)
    {
        using var repo = new FakeGitRepo();
        Assert.Equal(new RiskAssessment(RiskLevel.High, impact), Git(repo.Dir, argv));
    }

    [Fact]
    public void Other_pushes_are_normal_with_an_impact_line()
    {
        using var repo = new FakeGitRepo(branch: "main");
        Assert.Equal(new RiskAssessment(RiskLevel.Normal, "Pushes to main of owner/repo."), Git(repo.Dir, "push"));
        Assert.Equal(RiskLevel.High, Git(repo.Dir, "push", "-f").Level);
        Assert.Equal("Force-pushes to topic of owner/repo. Commits on the remote can be lost.", Git(repo.Dir, "push", "-f", "origin", "topic").Impact);
        Assert.Equal("Deletes branch topic of owner/repo.", Git(repo.Dir, "push", "origin", "--delete", "topic").Impact);
        Assert.Equal(RiskAssessment.Plain, Git(repo.Dir, "status"));
        Assert.Equal(RiskLevel.High, Git(Path.GetTempPath(), "-C", repo.Dir, "push", "--force").Level);
    }

    [Fact]
    public void Without_a_known_default_branch_nothing_is_low_risk()
    {
        using var repo = new FakeGitRepo(remoteHead: null);
        Assert.Equal(RiskLevel.Normal, Git(repo.Dir, "push").Level);
        Assert.Equal(RiskLevel.Normal, Git(Path.Combine(Path.GetTempPath(), "no-repo-" + Guid.NewGuid().ToString("N")), "push").Level);

        File.WriteAllText(Path.Combine(repo.Dir, ".git", "packed-refs"), "# pack-refs\nabc123 refs/remotes/origin/master\n");
        Assert.Equal("Pushes to feature-x of owner/repo. Not the default branch (master).", Git(repo.Dir, "push").Impact);
    }

    [Theory]
    [InlineData("git@github.com:owner/repo.git")]
    [InlineData("ssh://git@github.com/owner/repo")]
    [InlineData("https://github.example.test/owner/repo.git")]
    public void Repo_name_comes_from_the_remote_url(string url)
    {
        using var repo = new FakeGitRepo(url: url);
        Assert.Equal("owner/repo", GitRepoInfo.TryRead(Path.Combine(repo.Dir))!.RepoName("origin"));
    }

    [Fact]
    public void A_local_remote_shows_its_folder_name_from_the_escaped_config()
    {
        // git writes C:\repos\remote.git as C:\\repos\\remote.git in .git/config.
        using var repo = new FakeGitRepo(url: @"C:\\repos\\remote.git");
        var info = GitRepoInfo.TryRead(repo.Dir)!;
        Assert.Equal(@"C:\repos\remote.git", info.RemoteUrls["origin"]);
        Assert.Equal("remote", info.RepoName("origin"));
    }

    [Fact]
    public void A_worktree_reads_the_common_config()
    {
        using var repo = new FakeGitRepo();
        var worktreeGit = Path.Combine(repo.Dir, ".git", "worktrees", "wt");
        Directory.CreateDirectory(worktreeGit);
        File.WriteAllText(Path.Combine(worktreeGit, "HEAD"), "ref: refs/heads/wt-branch\n");
        File.WriteAllText(Path.Combine(worktreeGit, "commondir"), "../..\n");
        var worktree = Path.Combine(repo.Dir, "wt");
        Directory.CreateDirectory(worktree);
        File.WriteAllText(Path.Combine(worktree, ".git"), $"gitdir: {worktreeGit}\n");

        Assert.Equal("Pushes to wt-branch of owner/repo. Not the default branch (main).", Git(worktree, "push", "origin", "wt-branch").Impact);
    }

    [Fact]
    public void Gh_and_az_deletes_are_high_risk_and_merge_names_the_pr()
    {
        using var repo = new FakeGitRepo();
        Assert.Equal(new RiskAssessment(RiskLevel.High, "Deletes the repository owner/other. You cannot undo this."),
            RiskAssessor.Assess("gh", ["repo", "delete", "owner/other", "--yes"], CommandClass.Write, repo.Dir));
        Assert.Equal("Deletes release v1.0 in team/app. You cannot undo this.",
            RiskAssessor.Assess("gh", ["release", "delete", "v1.0", "-R", "team/app"], CommandClass.Write, repo.Dir).Impact);
        Assert.Equal(new RiskAssessment(RiskLevel.Normal, "Merges pull request 51 in owner/repo."),
            RiskAssessor.Assess("gh", ["pr", "merge", "51", "--squash"], CommandClass.Write, repo.Dir));
        Assert.Equal(RiskLevel.High, RiskAssessor.Assess("az", ["group", "delete", "-n", "rg1", "--yes"], CommandClass.Write, null).Level);
        Assert.Equal(RiskAssessment.Plain, RiskAssessor.Assess("gh", ["pr", "list"], CommandClass.Read, repo.Dir));
    }

    [Fact]
    public void Secret_reveal_says_which_value_it_shows()
    {
        Assert.Equal("Shows the GH_TOKEN value to the app that runs this.",
            RiskAssessor.Assess("gh", ["auth", "token"], CommandClass.SecretReveal, null, "GH_TOKEN").Impact);
        Assert.Equal("push", RiskAssessor.Verb("git", ["-C", "x", "push", "--force"]));
        Assert.Equal("pr merge", RiskAssessor.Verb("gh", ["-R", "o/r", "pr", "merge", "5"]));
    }

    [Fact]
    public void Card_starts_with_the_impact_and_a_high_risk_card_offers_no_session()
    {
        var request = new ApprovalRequest("git", "write", "Trusted", "key", "authenticode", null, "", "authorize", LauncherEnrollmentKindNames.AiHarness, null,
            CommandLine: "git push --force origin main", Impact: "Force-pushes to main of owner/repo. You cannot undo this.", ImpactHigh: true);
        var payload = ApprovalPresentation.ToHelperPayload(request);
        Assert.True(payload.ImpactHigh);
        Assert.False(payload.SessionAllowOffered);
        Assert.Equal("Write command", payload.ReasonHeading);
        Assert.Equal("git gets no secret from the vault for this write command.", payload.ReasonLine);
        Assert.StartsWith("Force-pushes to main", ApprovalPromptText.BuildBody(request));
        Assert.True(ApprovalPresentation.ToHelperPayload(request with { ImpactHigh = false }).SessionAllowOffered);
    }
}
