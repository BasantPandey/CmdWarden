using System.Security.Cryptography;
using System.Text;
using CmdWarden.Cli;
using CmdWarden.Contracts;
using CmdWarden.Contracts.GitHub;

namespace CmdWarden.Tests;

/// <summary>
/// #40: an allowed gh run on one repo gets a GitHub App token for that repo only, from a fake
/// GitHub API. Other commands and repos without the app get the personal token.
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class GitHubAppTokenProcessTests
{
    [Fact]
    public async Task Gh_gets_a_repo_token_and_falls_back_to_the_personal_token()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var appKey = RSA.Create(2048);
        await using var github = new FakeGitHubApi(appKey);
        await using var agent = await TestAgent.StartAsync();
        if (agent is null)
            return;
        var root = agent.ProductRoot;
        GitHubApp.Save(new GitHubAppConfig { AppId = 7, Slug = "cmdwarden-test", ApiUrl = github.Url }, root);
        var vault = new CredentialVault();
        vault.SaveTarget(GitHubApp.KeyTarget, "7", appKey.ExportPkcs8PrivateKey());

        var output = Path.Combine(root, "gh-out.txt");
        var realGh = Path.Combine(root, "real-gh.cmd");
        await File.WriteAllTextAsync(realGh, $"""
            @echo off
            >"{output}" echo [%GH_TOKEN%]
            """);
        new ToolPinStore(root).Save("gh", realGh);
        agent.Enroll(LauncherEnrollmentKind.Terminal);
        var personal = "ghp_personal_" + Guid.NewGuid().ToString("N");
        await AgentVaultClient.SaveAsync("GH_TOKEN", Encoding.UTF8.GetBytes(personal), agent.PipeName);
        var previous = Environment.GetEnvironmentVariable("GH_TOKEN");
        Environment.SetEnvironmentVariable("GH_TOKEN", null);
        try
        {
            var stderr = new StringWriter();
            async Task<string> Gh(params string[] args)
            {
                File.Delete(output);
                var previousError = Console.Error;
                Console.SetError(stderr);
                try
                {
                    Assert.Equal(0, await GhShimApp.RunAsync(args, pipeName: agent.PipeName, timeout: TimeSpan.FromSeconds(30)));
                }
                finally
                {
                    Console.SetError(previousError);
                }
                return (await File.ReadAllTextAsync(output)).Trim();
            }

            Assert.True("[ghs_fake_1]" == await Gh("pr", "list", "-R", "owner/repo"), stderr.ToString());
            Assert.Equal("""{"repositories":["repo"]}""", github.LastTokenBody);
            // The agent keeps the token until five minutes before it ends.
            Assert.Equal("[ghs_fake_1]", await Gh("issue", "list", "--repo", "owner/repo"));
            Assert.Equal(1, github.TokensCreated);

            // Not on one repo, or the app is not installed there: the personal token.
            Assert.Equal($"[{personal}]", await Gh("api", "user"));
            Assert.Equal($"[{personal}]", await Gh("pr", "list", "-R", "owner/missing"));
            // The shim says why the app token was not used.
            Assert.Contains("the GitHub App is not installed on owner/missing", stderr.ToString(), StringComparison.Ordinal);
            Assert.Equal($"[{personal}]", await Gh("pr", "list", "-R", "gitlab.com/owner/repo"));

            var audit = string.Join("\n", Directory.GetFiles(Path.Combine(root, "audit"), "gates-*.ndjson").Select(File.ReadAllText));
            Assert.Contains(GitHubApp.TokenLabel("owner/repo"), audit, StringComparison.Ordinal);
            Assert.Contains(PolicyReasonCodes.AppTokenFallback, audit, StringComparison.Ordinal);
            Assert.DoesNotContain("ghs_fake", audit, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GH_TOKEN", previous);
            vault.DeleteTarget(GitHubApp.KeyTarget);
            try { await AgentVaultClient.DeleteAsync("GH_TOKEN", agent.PipeName); } catch { /* ignore */ }
        }
    }

    [Theory]
    [InlineData("pr list -R owner/repo", "owner/repo")]
    [InlineData("pr view 5 --repo=github.com/o/r", "o/r")]
    [InlineData("pr list -R https://github.com/o/r.git", "o/r")]
    [InlineData("pr list -R git@github.com:o/r.git", "o/r")]
    [InlineData("repo view -R o/r", "o/r")]
    [InlineData("repo view other/x -R o/r", null)]
    [InlineData("api user -R o/r", null)]
    [InlineData("auth status", null)]
    [InlineData("pr list -R ghe.example.com/o/r", null)]
    public void Repo_of_a_gh_command(string argv, string? expected) =>
        Assert.Equal(expected, GitHubApp.RepoFor(argv.Split(' '), null, null));

    [Fact]
    public void Gh_repo_env_names_the_repo() =>
        Assert.Equal("o/r", GitHubApp.RepoFor(["issue", "list"], "o/r", null));
}
