using CmdWarden.Cli.Harden;

namespace CmdWarden.Tests;

public class ShimPayloadTests
{
    [Fact]
    public void Install_copies_only_the_named_exes_and_every_shared_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-payload-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "shim-payload");
        var shims = Path.Combine(root, "shims");
        Directory.CreateDirectory(source);
        foreach (var name in new[] { "az.exe", "az.dll", "gh.exe", "gh.dll", "git.exe", "CmdWarden.Contracts.dll", "az.runtimeconfig.json" })
            File.WriteAllText(Path.Combine(source, name), name);
        try
        {
            ShimPayload.Install(source, shims, "az.exe");

            var installed = Directory.GetFiles(shims).Select(Path.GetFileName).Order().ToList();
            Assert.Equal(["az.dll", "az.exe", "az.runtimeconfig.json", "CmdWarden.Contracts.dll", "gh.dll"], installed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
