using CmdWarden.Cli.Scan;
using CmdWarden.Contracts.Scan;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// First-catalog scan detectors (issue #43).
/// </summary>
public class ScanTests
{
    [Fact]
    public void GhAmbientToken_reports_when_env_set_without_value()
    {
        var secret = "ghp_super_secret_" + Guid.NewGuid().ToString("N");
        var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["GH_TOKEN"] = secret,
        };

        var ctx = new ScanContext(
            productRoot: Path.Combine(Path.GetTempPath(), "cw-scan-" + Guid.NewGuid().ToString("N")),
            pathEnv: "",
            getEnv: name => env.TryGetValue(name, out var v) ? v : null);

        var findings = new GhAmbientTokenDetector().Detect(ctx);
        Assert.Single(findings);
        var f = findings[0];
        Assert.Equal("gh.ambient_token", f.Id);
        Assert.Equal("gh", f.Tool);
        Assert.Equal(ScanSeverity.High, f.Severity);
        Assert.Contains("GH_TOKEN", f.Evidence, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, f.Evidence, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, f.Summary, StringComparison.Ordinal);
        Assert.Equal("cw harden gh", f.HardenHint);

        var formatted = ScanFormatter.Format(findings);
        Assert.DoesNotContain(secret, formatted, StringComparison.Ordinal);
        Assert.Contains("harden_hint", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void GhAmbientToken_silent_when_unset()
    {
        var ctx = new ScanContext(
            productRoot: Path.Combine(Path.GetTempPath(), "cw-scan-" + Guid.NewGuid().ToString("N")),
            pathEnv: "",
            getEnv: _ => null);

        Assert.Empty(new GhAmbientTokenDetector().Detect(ctx));
    }

    [Fact]
    public void GhNotHardened_detects_real_gh_on_isolated_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-scan-gh-" + Guid.NewGuid().ToString("N"));
        var bin = Path.Combine(root, "bin");
        Directory.CreateDirectory(bin);
        var fakeGh = Path.Combine(bin, "gh.exe");
        // Non-zero length so discoverer does not skip App Execution Alias stubs.
        File.WriteAllText(fakeGh, "fake-gh");

        try
        {
            var ctx = new ScanContext(
                productRoot: Path.Combine(root, "product"),
                pathEnv: bin,
                getEnv: _ => null);

            var findings = new GhNotHardenedDetector().Detect(ctx);
            Assert.Single(findings);
            Assert.Equal("gh.not_hardened", findings[0].Id);
            Assert.Equal("cw harden gh", findings[0].HardenHint);
            Assert.DoesNotContain("fake-gh", findings[0].Evidence, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void GhNotHardened_silent_when_pinned()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-scan-pin-" + Guid.NewGuid().ToString("N"));
        var bin = Path.Combine(root, "bin");
        var product = Path.Combine(root, "product");
        Directory.CreateDirectory(bin);
        var fakeGh = Path.Combine(bin, "gh.exe");
        File.WriteAllText(fakeGh, "fake-gh");

        try
        {
            new ToolPinStore(product).Save("gh", fakeGh);
            var ctx = new ScanContext(productRoot: product, pathEnv: bin, getEnv: _ => null);
            Assert.Empty(new GhNotHardenedDetector().Detect(ctx));

            // Residual absolute-path finding only when pin exists.
            var residual = new GhAbsolutePathResidualDetector().Detect(ctx);
            Assert.Single(residual);
            Assert.Equal("gh.absolute_path_residual", residual[0].Id);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void DockerAmbientAuthConfig_never_echoes_json_value()
    {
        var authJson = """{"auths":{"https://index.docker.io/v1/":{"auth":"dGVzdDpzZWNyZXQ="}}}""";
        var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["DOCKER_AUTH_CONFIG"] = authJson,
        };
        var ctx = new ScanContext(
            productRoot: Path.Combine(Path.GetTempPath(), "cw-scan-d-" + Guid.NewGuid().ToString("N")),
            getEnv: name => env.TryGetValue(name, out var v) ? v : null);

        var findings = new DockerAmbientAuthConfigDetector().Detect(ctx);
        Assert.Single(findings);
        Assert.DoesNotContain("dGVzdDpzZWNyZXQ=", findings[0].Evidence, StringComparison.Ordinal);
        Assert.DoesNotContain(authJson, findings[0].Summary, StringComparison.Ordinal);
        Assert.Contains("DOCKER_AUTH_CONFIG is set", findings[0].Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void GitCredentialStoreFile_detects_fixture_file_without_reading_contents()
    {
        var home = Path.Combine(Path.GetTempPath(), "cw-scan-home-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        var store = Path.Combine(home, ".git-credentials");
        var secretLine = "https://user:supersecretpassword@github.com";
        File.WriteAllText(store, secretLine + Environment.NewLine);

        try
        {
            var ctx = new ScanContext(
                productRoot: Path.Combine(home, "product"),
                userProfile: home,
                getEnv: _ => null);

            var findings = new GitCredentialStoreFileDetector().Detect(ctx);
            Assert.Single(findings);
            Assert.Equal("git.credential_store_file", findings[0].Id);
            Assert.DoesNotContain("supersecretpassword", findings[0].Evidence, StringComparison.Ordinal);
            Assert.DoesNotContain(secretLine, findings[0].Summary, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(home, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ScanEngine_runs_defaults_and_includes_system_scope()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-scan-eng-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var engine = new ScanEngine();
            var findings = engine.Run(new ScanContext(
                productRoot: root,
                pathEnv: "",
                getEnv: _ => null,
                userProfile: root));

            Assert.Contains(findings, f => f.Id == "system.scan_scope");
            Assert.All(findings, f =>
            {
                Assert.False(string.IsNullOrWhiteSpace(f.Id));
                Assert.False(string.IsNullOrWhiteSpace(f.Tool));
                Assert.False(string.IsNullOrWhiteSpace(f.Severity));
                Assert.False(string.IsNullOrWhiteSpace(f.Title));
            });

            var text = ScanFormatter.Format(findings);
            Assert.Contains("findings:", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ghp_", text, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ScanEngine_never_auto_hardens_or_creates_pins()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-scan-noharden-" + Guid.NewGuid().ToString("N"));
        var bin = Path.Combine(root, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "gh.exe"), "x");
        var product = Path.Combine(root, "product");

        try
        {
            var engine = new ScanEngine();
            _ = engine.Run(new ScanContext(productRoot: product, pathEnv: bin, getEnv: _ => null));
            Assert.Null(new ToolPinStore(product).TryGet("gh"));
            Assert.False(Directory.Exists(Path.Combine(product, "shims")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Order_is_severity_then_catalog_tool_then_id_with_scope_banner_last()
    {
        static ScanFinding F(string id, string tool, string sev) => new(id, tool, sev, "t", "s", "e");
        var shuffled = new[]
        {
            F(SystemScanBannerDetector.FindingId, "system", ScanSeverity.Info),
            F("docker.b", "docker", ScanSeverity.Low),
            F("gh.b", "gh", ScanSeverity.Medium),
            F("az.a", "az", ScanSeverity.High),
            F("gh.a", "gh", ScanSeverity.Medium),
            F("gh.z", "gh", ScanSeverity.High),
            F("git.a", "git", ScanSeverity.Info),
        };

        var ordered = ScanEngine.Order(shuffled).Select(f => f.Id).ToArray();

        Assert.Equal(
            new[] { "gh.z", "az.a", "gh.a", "gh.b", "docker.b", "git.a", SystemScanBannerDetector.FindingId },
            ordered);
    }
}
