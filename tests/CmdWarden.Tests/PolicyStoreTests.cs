using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Seam: PolicyStore - enrollment + level resolve for tool x launcher (issue #5).
/// </summary>
public class PolicyStoreTests
{
    [Fact]
    public void Defaults_are_AI_Read_and_terminal_Trusted()
    {
        var store = PolicyStore.CreateEmpty();
        Assert.Equal(PolicyLevel.Read, store.DefaultAiHarnessLevel);
        Assert.Equal(PolicyLevel.Trusted, store.DefaultTerminalLevel);
    }

    [Fact]
    public void Unknown_or_ineligible_launcher_resolves_to_Deny()
    {
        var store = PolicyStore.CreateEmpty();
        store.Enroll("auth:sha1:abc", LauncherEnrollmentKind.Terminal);

        var unknown = store.ResolveLevel("gh", LauncherKinds.PolicyKeyUnknown, autoApproveEligible: false);
        Assert.Equal(PolicyLevel.Deny, unknown.Level);
        Assert.Equal(PolicyReasonCodes.UnknownLauncher, unknown.ReasonCode);

        var ineligible = store.ResolveLevel("gh", "auth:sha1:abc", autoApproveEligible: false);
        Assert.Equal(PolicyLevel.Deny, ineligible.Level);
        Assert.Equal(PolicyReasonCodes.UnknownLauncher, ineligible.ReasonCode);
    }

    [Fact]
    public void Unenrolled_eligible_launcher_resolves_to_Deny()
    {
        var store = PolicyStore.CreateEmpty();
        var result = store.ResolveLevel("gh", "auth:sha1:deadbeef", autoApproveEligible: true);
        Assert.Equal(PolicyLevel.Deny, result.Level);
        Assert.Equal(PolicyReasonCodes.NotEnrolled, result.ReasonCode);
        Assert.False(result.IsEnrolled);
    }

    [Fact]
    public void Enrolled_terminal_defaults_to_Trusted_for_any_tool()
    {
        var store = PolicyStore.CreateEmpty();
        store.Enroll("auth:sha1:term", LauncherEnrollmentKind.Terminal);

        var result = store.ResolveLevel("gh", "auth:sha1:term", autoApproveEligible: true);
        Assert.Equal(PolicyLevel.Trusted, result.Level);
        Assert.True(result.IsEnrolled);
        Assert.Equal(LauncherEnrollmentKind.Terminal, result.EnrollmentKind);
    }

    [Fact]
    public void Enrolled_ai_harness_defaults_to_Read()
    {
        var store = PolicyStore.CreateEmpty();
        store.Enroll("pathhash:sha256:abc", LauncherEnrollmentKind.AiHarness);

        var result = store.ResolveLevel("gh", "pathhash:sha256:abc", autoApproveEligible: true);
        Assert.Equal(PolicyLevel.Read, result.Level);
        Assert.Equal(LauncherEnrollmentKind.AiHarness, result.EnrollmentKind);
    }

    [Fact]
    public void Explicit_tool_level_overrides_kind_default()
    {
        var store = PolicyStore.CreateEmpty();
        store.Enroll("auth:sha1:x", LauncherEnrollmentKind.Terminal);
        store.SetLevel("auth:sha1:x", "gh", PolicyLevel.Full);

        Assert.Equal(PolicyLevel.Full, store.ResolveLevel("gh", "auth:sha1:x", true).Level);
        Assert.Equal(PolicyLevel.Trusted, store.ResolveLevel("git", "auth:sha1:x", true).Level);
    }

    [Fact]
    public void Round_trips_json_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cw-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "policy.json");
        try
        {
            var store = new PolicyStore(path);
            store.Enroll("auth:sha1:round", LauncherEnrollmentKind.AiHarness);
            store.SetLevel("auth:sha1:round", "inject", PolicyLevel.Full);
            store.Save();

            var reloaded = new PolicyStore(path);
            reloaded.Load();
            Assert.Equal(PolicyLevel.Full, reloaded.ResolveLevel("inject", "auth:sha1:round", true).Level);
            Assert.Equal(PolicyLevel.Read, reloaded.ResolveLevel("gh", "auth:sha1:round", true).Level);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Load_reports_a_changed_tool_level_and_a_removed_launcher()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cw-policy-diff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "policy.json");
        try
        {
            var writer = new PolicyStore(path);
            writer.Enroll("key-a", LauncherEnrollmentKind.Terminal);
            writer.SetLevel("key-a", "gh", PolicyLevel.Trusted);
            writer.Enroll("key-b", LauncherEnrollmentKind.Terminal);
            writer.Save();

            var reader = new PolicyStore(path);
            reader.Load();

            // Tighten gh under key-a, and remove key-b entirely (unenroll).
            writer.SetLevel("key-a", "gh", PolicyLevel.Deny);
            writer.Unenroll("key-b");
            writer.Save();

            var changes = reader.Load();

            Assert.Contains(changes, c => c.LauncherPolicyKey == "key-a" && c.Tool == "gh");
            Assert.Contains(changes, c => c.LauncherPolicyKey == "key-b" && c.Tool is null);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Load_reports_no_changes_when_the_file_is_unchanged()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cw-policy-nodiff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "policy.json");
        try
        {
            var store = new PolicyStore(path);
            store.Enroll("key-a", LauncherEnrollmentKind.Terminal);
            store.SetLevel("key-a", "gh", PolicyLevel.Trusted);
            store.Save();
            store.Load();

            var changes = store.Load();

            Assert.Empty(changes);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Configurable_defaults_apply_to_new_enrollments()
    {
        var store = PolicyStore.CreateEmpty();
        store.SetDefaults(aiHarness: PolicyLevel.Deny, terminal: PolicyLevel.Full);
        store.Enroll("auth:sha1:t", LauncherEnrollmentKind.Terminal);
        store.Enroll("auth:sha1:a", LauncherEnrollmentKind.AiHarness);

        Assert.Equal(PolicyLevel.Full, store.ResolveLevel("gh", "auth:sha1:t", true).Level);
        Assert.Equal(PolicyLevel.Deny, store.ResolveLevel("gh", "auth:sha1:a", true).Level);
    }
}
