using CmdWarden.Contracts;

namespace CmdWarden.Tests;

public class ToolPackTests
{
    private const string SamplePack = """
        {
          "tool": "sample",
          "binaries": ["sample.cmd"],
          "secretEnv": ["SAMPLE_TOKEN"],
          "flagsWithValue": ["--profile"],
          "rules": [
            { "match": "token show", "class": "secret-reveal" },
            { "match": "list|ls", "class": "read" },
            { "match": "deploy --dry-run", "class": "read" },
            { "match": "deploy", "class": "write", "risk": "high" },
            { "match": "run", "class": "write", "secrets": false }
          ],
          "samples": [ { "argv": "--profile list deploy", "class": "write" } ]
        }
        """;

    [Fact]
    public void Built_in_packs_load_and_pass_their_samples()
    {
        var tools = ToolPacks.BuiltIn.Select(p => p.Tool).ToList();
        Assert.Equal(["aws", "kubectl", "npm"], tools);
        Assert.All(ToolPacks.BuiltIn, p =>
        {
            Assert.NotEmpty(p.Samples);
            Assert.Empty(p.Validate());
        });
    }

    [Fact]
    public void The_example_pack_in_the_docs_loads_and_passes_its_samples()
    {
        var doc = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "docs", "tool-packs.md"));
        var start = doc.IndexOf("```json", StringComparison.Ordinal) + "```json".Length;
        var pack = ToolPacks.Parse(doc[start..doc.IndexOf("```", start, StringComparison.Ordinal)], "docs/tool-packs.md");
        Assert.Equal("terraform", pack.Tool);
        Assert.Equal(CommandClass.SecretReveal, pack.Classify(["output", "-json"]));
    }

    [Theory]
    [InlineData("token show", CommandClass.SecretReveal)]
    [InlineData("LS", CommandClass.Read)]
    [InlineData("--profile deploy list", CommandClass.Read)]
    [InlineData("deploy --dry-run", CommandClass.Read)]
    [InlineData("deploy --dry-run=true", CommandClass.Read)]
    [InlineData("deploy app", CommandClass.Write)]
    [InlineData("deploy --help", CommandClass.Read)]
    [InlineData("--version", CommandClass.Read)]
    [InlineData("help deploy", CommandClass.Read)]
    [InlineData("other", CommandClass.Unknown)]
    [InlineData("run -- list", CommandClass.Write)]
    public void Rules_match_words_flags_and_the_first_rule_wins(string argv, CommandClass expected)
    {
        var pack = ToolPacks.Parse(SamplePack, "test");
        Assert.Equal(expected, pack.Classify(argv.Split(' ')));
    }

    [Fact]
    public void Secrets_go_to_matched_runs_but_not_to_help_or_rules_that_say_no()
    {
        var pack = ToolPacks.Parse(SamplePack, "test");
        Assert.True(pack.ReleasesSecrets(["deploy", "app"]));
        Assert.True(pack.ReleasesSecrets(["other"]));
        Assert.False(pack.ReleasesSecrets(["run", "build"]));
        Assert.False(pack.ReleasesSecrets(["deploy", "--help"]));
        Assert.Equal(RiskLevel.High, pack.Assess(["deploy", "app"]).Level);
        Assert.Equal(RiskLevel.Normal, pack.Assess(["list"]).Level);
    }

    [Fact]
    public void Npm_install_gets_the_token_only_without_install_scripts()
    {
        var npm = ToolPacks.BuiltIn.Single(p => p.Tool == "npm");
        Assert.False(npm.ReleasesSecrets(["install"]));
        Assert.False(npm.ReleasesSecrets(["run", "test"]));
        Assert.True(npm.ReleasesSecrets(["ci", "--ignore-scripts"]));
        Assert.True(npm.ReleasesSecrets(["publish"]));
    }

    [Theory]
    [InlineData("""{ "tool": "gh", "binaries": ["gh.exe"] }""", "built in")]
    [InlineData("""{ "tool": "Bad Name", "binaries": ["x.exe"] }""", "lower case")]
    [InlineData("""{ "tool": "x", "binaries": ["..\\x.exe"] }""", "file name")]
    [InlineData("""{ "tool": "x", "binaries": ["x.ps1"] }""", "file name")]
    [InlineData("""{ "tool": "x", "binaries": ["x.exe"], "rules": [ { "match": "a", "class": "maybe" } ] }""", "not a class")]
    [InlineData("""{ "tool": "x", "binaries": ["x.exe"], "secretEnv": ["A B"] }""", "vault name")]
    [InlineData("""{ "tool": "x", "binaries": ["x.exe"], "samples": [ { "argv": "a", "class": "read" } ] }""", "sample")]
    [InlineData("""{ "tool": "x", """, "not valid JSON")]
    public void A_bad_pack_does_not_load(string json, string reason)
    {
        var ex = Assert.Throws<InvalidDataException>(() => ToolPacks.Parse(json, "bad.json"));
        Assert.Contains(reason, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void User_packs_join_the_catalog_and_a_bad_file_is_reported()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-packs-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(ToolPacks.UserDir(root));
            File.WriteAllText(Path.Combine(ToolPacks.UserDir(root), "sample.json"), SamplePack);
            File.WriteAllText(Path.Combine(ToolPacks.UserDir(root), "broken.json"), "{");

            var (packs, errors) = ToolPacks.Load(root);
            Assert.Contains(packs, p => p.Tool == "sample" && p.Source.EndsWith("sample.json", StringComparison.Ordinal));
            Assert.Single(errors);
            Assert.EndsWith("broken.json", errors[0].Source, StringComparison.Ordinal);
            Assert.Contains(ToolCatalog.All(root), e => e.Id == "sample");
            Assert.Null(ToolPacks.Find("gh", root));
            Assert.NotNull(ToolPacks.Find("SAMPLE", root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
