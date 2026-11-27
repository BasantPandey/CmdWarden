using CmdWarden.Cli.Hooks;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>Reviewed scripts (issue 201): bless, allow, edit, and refuse.</summary>
public class BlessedScriptTests
{
    [Fact]
    public void Bless_stores_path_hash_and_caps()
    {
        var dir = TempDir();
        var script = Write(dir, "release.ps1", "Write-Output notes\r\n");
        var store = new BlessedScriptStore(Path.Combine(dir, "blessed-scripts.json"));

        store.Save(new ScriptBlessing
        {
            Path = script,
            Sha256 = BoundFiles.Sha256(script),
            Caps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["gh"] = "write", ["aws"] = "read" },
        });

        var saved = Assert.Single(store.Load());
        Assert.Equal(script, saved.Path);
        Assert.Equal(BoundFiles.Sha256(script), saved.Sha256);
        Assert.Equal("write", saved.Caps["gh"]);
        Assert.Equal("read", saved.Caps["aws"]);
    }

    [Fact]
    public void Matching_script_allows_gh_release_create()
    {
        var dir = TempDir();
        var script = Write(dir, "release.ps1", "gh release create v1\r\n");
        var blessings = Bless(script, "gh", "write");
        var ancestors = new[] { new AncestorCommand("pwsh.exe", ["-File", script]) };

        Assert.True(BlessedScriptMatch.Allows(blessings, ancestors, "gh", CommandClass.Write, BoundFiles.Sha256));
    }

    [Fact]
    public void Agent_command_with_no_script_does_not_allow()
    {
        var dir = TempDir();
        var script = Write(dir, "release.ps1", "gh release create v1\r\n");
        var blessings = Bless(script, "gh", "write");
        var ancestors = new[] { new AncestorCommand("Codex.exe", ["--sandbox"]) };

        Assert.False(BlessedScriptMatch.Allows(blessings, ancestors, "gh", CommandClass.Write, BoundFiles.Sha256));
    }

    [Fact]
    public void One_byte_change_clears_the_review()
    {
        var dir = TempDir();
        var script = Write(dir, "release.ps1", "gh release create v1\r\n");
        var blessings = Bless(script, "gh", "write");
        File.AppendAllText(script, " ");
        var ancestors = new[] { new AncestorCommand("powershell.exe", ["-File", script]) };

        Assert.False(BlessedScriptMatch.Allows(blessings, ancestors, "gh", CommandClass.Write, BoundFiles.Sha256));
    }

    [Fact]
    public void Read_cap_refuses_a_write()
    {
        var dir = TempDir();
        var script = Write(dir, "list.ps1", "gh issue list\r\n");
        var blessings = Bless(script, "gh", "read");
        var ancestors = new[] { new AncestorCommand("pwsh", ["-File", script]) };

        Assert.True(BlessedScriptMatch.Allows(blessings, ancestors, "gh", CommandClass.Read, BoundFiles.Sha256));
        Assert.False(BlessedScriptMatch.Allows(blessings, ancestors, "gh", CommandClass.Write, BoundFiles.Sha256));
    }

    [Fact]
    public void Secret_reveal_still_asks()
    {
        var dir = TempDir();
        var script = Write(dir, "token.ps1", "gh auth token\r\n");
        var blessings = Bless(script, "gh", "write");
        var ancestors = new[] { new AncestorCommand("pwsh", ["-File", script]) };

        Assert.False(BlessedScriptMatch.Allows(blessings, ancestors, "gh", CommandClass.SecretReveal, BoundFiles.Sha256));
    }

    [Fact]
    public void Hook_blocks_the_agent_from_blessing_a_script()
    {
        Assert.Contains("cw script bless", PolicyHook.WeakeningCwCalls("cw script bless .\\release.ps1 --cap gh=write"));
        Assert.Contains("cw script drop", PolicyHook.WeakeningCwCalls("cw script drop .\\release.ps1"));
    }

    private static IReadOnlyList<ScriptBlessing> Bless(string path, string tool, string cap) =>
    [
        new ScriptBlessing
        {
            Path = path,
            Sha256 = BoundFiles.Sha256(path),
            Caps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [tool] = cap },
        },
    ];

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cw-bless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Write(string dir, string name, string text)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, text);
        return path;
    }
}
