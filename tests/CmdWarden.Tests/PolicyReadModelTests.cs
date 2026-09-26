using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Seam: PolicyReadModel over a temp policy file (issue #119).
/// </summary>
public class PolicyReadModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cw-prm-" + Guid.NewGuid().ToString("N"));
    private string PolicyPath => Path.Combine(_dir, "policy.json");

    public PolicyReadModelTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private PolicyStore NewStore()
    {
        var store = new PolicyStore(PolicyPath);
        store.Load();
        return store;
    }

    [Fact]
    public void RecentUnenrolled_lists_each_unenrolled_key_once_newest_first()
    {
        var store = NewStore();
        store.Enroll("enrolled", LauncherEnrollmentKind.Terminal);
        AuditGateRecord Row(string key, string? path, int minutesAgo) => new()
        {
            Ts = DateTime.UtcNow.AddMinutes(-minutesAgo).ToString("o"),
            Decision = GateDecisions.Deny,
            Tool = "gh",
            LauncherPolicyKey = key,
            LauncherPath = path,
        };

        var seen = PolicyReadModel.RecentUnenrolled(
            [Row("new", @"C:\claude.exe", 1), Row("enrolled", null, 2), Row(LauncherKinds.PolicyKeyUnknown, null, 3),
             Row("new", @"C:\old.exe", 4), Row("older", null, 5), Row("", null, 6)],
            store.Launchers);

        Assert.Equal(["new", "older"], seen.Select(s => s.PolicyKey));
        Assert.Equal(@"C:\claude.exe", seen[0].Path);
        Assert.Null(seen[1].Path);
    }

    [Fact]
    public void Missing_file_gives_defaults_and_no_launchers()
    {
        var m = PolicyReadModel.Load(PolicyPath);
        Assert.Equal(PolicyPath, m.Path);
        Assert.Equal(PolicyLevel.Read, m.AiHarnessDefault);
        Assert.Equal(PolicyLevel.Trusted, m.TerminalDefault);
        Assert.Empty(m.Launchers);
    }

    [Fact]
    public void Launcher_without_overrides_inherits_kind_default_on_every_catalog_tool()
    {
        var store = NewStore();
        store.Enroll("pathhash:sha256:abc", LauncherEnrollmentKind.AiHarness, @"C:\Tools\cursor.exe");
        store.Save();

        var l = Assert.Single(PolicyReadModel.Load(PolicyPath).Launchers);
        Assert.Equal("pathhash:sha256:abc", l.PolicyKey);
        Assert.Equal(LauncherEnrollmentKind.AiHarness, l.Kind);
        Assert.Equal(@"C:\Tools\cursor.exe", l.DisplayPath);
        Assert.Equal(ToolCatalog.All().Select(t => t.Id), l.Tools.Select(t => t.Tool));
        Assert.All(l.Tools, t => Assert.Equal(PolicyLevel.Read, t.Level));
        Assert.All(l.Tools, t => Assert.False(t.IsOverride));
    }

    [Fact]
    public void Launcher_with_override_flags_only_that_tool()
    {
        var store = NewStore();
        store.Enroll("auth:sha1:term", LauncherEnrollmentKind.Terminal);
        store.SetLevel("auth:sha1:term", "gh", PolicyLevel.Full);
        store.SetLevel("auth:sha1:term", "docker", PolicyLevel.Deny);
        store.Save();

        var l = Assert.Single(PolicyReadModel.Load(PolicyPath).Launchers);
        Assert.Null(l.DisplayPath);
        var gh = l.Tools.Single(t => t.Tool == "gh");
        var git = l.Tools.Single(t => t.Tool == "git");
        var docker = l.Tools.Single(t => t.Tool == "docker");
        Assert.Equal((PolicyLevel.Full, true), (gh.Level, gh.IsOverride));
        Assert.Equal((PolicyLevel.Trusted, false), (git.Level, git.IsOverride));
        Assert.Equal((PolicyLevel.Deny, true), (docker.Level, docker.IsOverride));
    }

    [Fact]
    public void Unknown_kind_matches_evaluator()
    {
        File.WriteAllText(PolicyPath,
            "{\"defaults\":{\"aiHarness\":\"Read\",\"terminal\":\"Trusted\"},\"launchers\":{\"auth:sha1:odd\":{\"kind\":\"martian\"}}}");

        var l = Assert.Single(PolicyReadModel.Load(PolicyPath).Launchers);
        Assert.Equal(LauncherEnrollmentKind.Unknown, l.Kind);
        var expected = NewStore().ResolveLevel("gh", "auth:sha1:odd", autoApproveEligible: true).Level;
        Assert.All(l.Tools, t => Assert.Equal(expected, t.Level));
    }

    [Fact]
    public void Malformed_json_throws_instead_of_defaults()
    {
        File.WriteAllText(PolicyPath, "{ this is not json");
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => PolicyReadModel.Load(PolicyPath));
    }

    [Fact]
    public void Enroll_without_path_does_not_write_a_path_key()
    {
        var store = NewStore();
        store.Enroll("auth:sha1:np", LauncherEnrollmentKind.Terminal);
        store.Save();
        Assert.DoesNotContain("\"path\"", File.ReadAllText(PolicyPath));
    }
}
