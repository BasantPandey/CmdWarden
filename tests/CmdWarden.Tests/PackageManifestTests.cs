using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace CmdWarden.Tests;

/// <summary>
/// Issue 64: the manifest script writes schema 1.28.0, and the install pages do not claim a live catalog.
/// </summary>
public class PackageManifestTests
{
    private const string PackageId = "BasantPandey.CmdWarden";

    [Fact]
    public void Manifest_script_writes_schema_1_28_for_the_portable_zip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = TestPaths.RepoRoot;
        var script = Path.Combine(root, "scripts", "New-PackageManifests.ps1");
        Assert.True(File.Exists(script), script);

        var work = Path.Combine(Path.GetTempPath(), "cw-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var zip = Path.Combine(work, "CmdWarden.9.9.9-win-x64.zip");
            File.WriteAllBytes(zip, "cmdwarden-manifest-fixture"u8.ToArray());
            var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip)));

            var firstOut = Path.Combine(work, "run1");
            var secondOut = Path.Combine(work, "run2");
            var firstLog = RunScript(root, script, zip, firstOut);
            var secondLog = RunScript(root, script, zip, secondOut);

            Assert.Equal(0, firstLog.ExitCode);
            Assert.Equal(0, secondLog.ExitCode);
            Assert.Contains(sha, firstLog.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(sha, secondLog.Text, StringComparison.OrdinalIgnoreCase);

            var firstSha = ShaFromLog(firstLog.Text);
            var secondSha = ShaFromLog(secondLog.Text);
            Assert.Equal(sha, firstSha);
            Assert.Equal(firstSha, secondSha);

            AssertManifests(Path.Combine(firstOut, "winget"), sha);
            AssertManifests(Path.Combine(secondOut, "winget"), sha);
        }
        finally
        {
            if (Directory.Exists(work))
                Directory.Delete(work, recursive: true);
        }
    }

    [Fact]
    public void Install_docs_match_the_live_install_path()
    {
        var root = TestPaths.RepoRoot;
        var readme = ReadRepo(root, "README.md");
        var index = ReadRepo(root, "docs", "index.md");
        var install = ReadRepo(root, "docs", "install.md");
        var packaging = ReadRepo(root, "packaging", "README.md");
        var signing = ReadRepo(root, "docs", "code-signing-policy.md");

        foreach (var page in new[] { readme, index, install })
        {
            Assert.Contains("setup zip", page, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("BasantPandey.CmdWarden", page, StringComparison.Ordinal);
            Assert.Contains("catalog does not have", page, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("NuGet.org does not have", page, StringComparison.OrdinalIgnoreCase);
            AssertNoLiveCatalogClaim(page);
        }

        Assert.Contains("schema 1.28.0", packaging, StringComparison.Ordinal);
        Assert.Contains("microsoft/winget-pkgs#442236", packaging, StringComparison.Ordinal);
        Assert.Contains("2026-10-30", packaging, StringComparison.Ordinal);
        Assert.Contains("use 1.28.0", packaging, StringComparison.Ordinal);
        Assert.DoesNotMatch(
            new Regex(@"submit[^.\n]{0,40}1\.12\.0", RegexOptions.IgnoreCase),
            packaging);

        Assert.Contains("application to SignPath Foundation is open", signing, StringComparison.Ordinal);
        Assert.Contains("Releases are not signed yet", signing, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"(?i)releases are signed"), signing);
    }

    private static void AssertManifests(string wingetDir, string sha)
    {
        var names = new[]
        {
            $"{PackageId}.yaml",
            $"{PackageId}.installer.yaml",
            $"{PackageId}.locale.en-US.yaml",
        };

        foreach (var name in names)
        {
            var text = File.ReadAllText(Path.Combine(wingetDir, name));
            Assert.Contains("ManifestVersion: 1.28.0", text, StringComparison.Ordinal);
            Assert.Contains("1.28.0.schema.json", text, StringComparison.Ordinal);
            Assert.Contains($"PackageIdentifier: {PackageId}", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ManifestVersion: 1.12.0", text, StringComparison.Ordinal);
            Assert.DoesNotContain("1.12.0", text, StringComparison.Ordinal);
        }

        var installer = File.ReadAllText(Path.Combine(wingetDir, $"{PackageId}.installer.yaml"));
        Assert.Contains(
            "https://aka.ms/winget-manifest.installer.1.28.0.schema.json",
            installer,
            StringComparison.Ordinal);
        Assert.Contains(
            "https://github.com/BasantPandey/CmdWarden/releases/download/v9.9.9/CmdWarden.9.9.9-win-x64.zip",
            installer,
            StringComparison.Ordinal);
        Assert.Contains("ArchiveBinariesDependOnPath: true", installer, StringComparison.Ordinal);
        Assert.Contains($"InstallerSha256: {sha}", installer, StringComparison.Ordinal);
        Assert.Contains("Commands:", installer, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"(?m)^  - cw\s*$"), installer);
        Assert.Matches(new Regex(@"(?m)^  - cmdwarden\s*$"), installer);
    }

    private static void AssertNoLiveCatalogClaim(string page)
    {
        Assert.DoesNotMatch(new Regex(@"(?i)winget already finds"), page);
        Assert.DoesNotMatch(new Regex(@"(?i)nuget\.org already"), page);
        Assert.DoesNotMatch(new Regex(@"(?i)smartscreen is clean"), page);
        Assert.DoesNotMatch(new Regex(@"(?i)binaries are signed"), page);
        Assert.DoesNotMatch(new Regex(@"(?im)^`?winget install (BasantPandey\.CmdWarden|CmdWarden)`?\s*$"), page);
        Assert.DoesNotMatch(new Regex(@"(?im)^`?dotnet tool install -g CmdWarden`?\s*$"), page);
    }

    private static string ReadRepo(string root, params string[] parts)
    {
        var path = Path.Combine(new[] { root }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), path);
        return File.ReadAllText(path);
    }

    private static string ShaFromLog(string text)
    {
        var match = Regex.Match(text, @"sha256 ([0-9A-F]{64})", RegexOptions.IgnoreCase);
        Assert.True(match.Success, text);
        return match.Groups[1].Value.ToUpperInvariant();
    }

    private static (int ExitCode, string Text) RunScript(string root, string script, string zip, string outDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add("-Version");
        psi.ArgumentList.Add("9.9.9");
        psi.ArgumentList.Add("-Zip");
        psi.ArgumentList.Add(zip);
        psi.ArgumentList.Add("-Out");
        psi.ArgumentList.Add(outDir);

        using var process = Process.Start(psi);
        Assert.NotNull(process);
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout + stderr);
    }
}
