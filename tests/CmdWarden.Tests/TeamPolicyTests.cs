using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>#47: team rules from a git repo only lower a level.</summary>
public class TeamPolicyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cw-team-" + Guid.NewGuid().ToString("N"));
    private string PolicyPath => Path.Combine(_dir, "policy.json");

    public TeamPolicyTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    private PolicyStore StoreWith(string? teamJson)
    {
        var store = new PolicyStore(PolicyPath);
        store.Enroll("harness-key", LauncherEnrollmentKind.AiHarness);
        store.Enroll("terminal-key", LauncherEnrollmentKind.Terminal);
        store.SetLevel("harness-key", "gh", PolicyLevel.Full);
        store.Save();
        if (teamJson is not null)
            File.WriteAllText(TeamPolicy.PathFor(PolicyPath), teamJson);
        store.Load();
        return store;
    }

    [Fact]
    public void A_team_rule_lowers_a_local_level_and_says_so()
    {
        var store = StoreWith("""{ "maxLevels": { "ai-harness": { "gh": "Read" } } }""");

        var gh = store.ResolveLevel("gh", "harness-key", autoApproveEligible: true);
        Assert.Equal(PolicyLevel.Read, gh.Level);
        Assert.True(gh.TeamCapped);
    }

    [Fact]
    public void A_team_rule_never_raises_a_level()
    {
        var store = StoreWith("""{ "maxLevels": { "ai-harness": { "*": "Full" }, "terminal": { "*": "Full" } } }""");

        var az = store.ResolveLevel("az", "harness-key", autoApproveEligible: true);
        Assert.Equal(PolicyLevel.Read, az.Level);
        Assert.False(az.TeamCapped);
        Assert.Equal(PolicyLevel.Deny, store.ResolveLevel("gh", "unenrolled", autoApproveEligible: true).Level);
    }

    [Fact]
    public void The_wildcard_caps_the_kind_default_and_a_tool_rule_comes_first()
    {
        var store = StoreWith("""{ "maxLevels": { "terminal": { "*": "Read", "git": "Trusted" } } }""");

        Assert.Equal(PolicyLevel.Read, store.ResolveLevel("az", "terminal-key", autoApproveEligible: true).Level);
        Assert.Equal(PolicyLevel.Trusted, store.ResolveLevel("git", "terminal-key", autoApproveEligible: true).Level);
        Assert.Equal(PolicyLevel.Full, store.ResolveLevel("gh", "harness-key", autoApproveEligible: true).Level);
    }

    [Theory]
    [InlineData("""{ "maxLevels": { "robot": { "*": "Read" } } }""")]
    [InlineData("""{ "maxLevels": { "terminal": { "*": "Maybe" } } }""")]
    [InlineData("""{ "maxLevels": [ ] }""")]
    [InlineData("not json")]
    public void A_bad_team_file_fails_instead_of_being_ignored(string json) =>
        Assert.Throws<InvalidDataException>(() => TeamPolicy.Parse(json));

    [Fact]
    public void New_team_rules_drop_the_approval_memory_of_every_launcher()
    {
        var store = StoreWith(null);
        Assert.Empty(store.Load());

        File.WriteAllText(TeamPolicy.PathFor(PolicyPath), """{ "maxLevels": { "ai-harness": { "*": "Deny" } } }""");
        var changes = store.Load();
        Assert.Equal(["harness-key", "terminal-key"], changes.Select(c => c.LauncherPolicyKey).Order());
        Assert.All(changes, c => Assert.Null(c.Tool));

        Assert.Empty(store.Load());
    }

    [Fact]
    public void Save_and_Load_keep_the_rules_and_the_source()
    {
        var team = TeamPolicy.Parse("""{ "maxLevels": { "ai_harness": { "gh": "read" } } }""");
        team.Source = "https://example.com/team.git";
        team.Commit = "abc123";
        team.Save(TeamPolicy.PathFor(PolicyPath));

        var loaded = TeamPolicy.Load(TeamPolicy.PathFor(PolicyPath))!;
        Assert.Equal(["ai_harness gh: max Read"], loaded.Lines());
        Assert.Equal("https://example.com/team.git", loaded.Source);
        Assert.Equal("abc123", loaded.Commit);
    }
}
