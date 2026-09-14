using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Locator for packaged Vault UI under secrets-manager/ (ticket #96).
/// </summary>
public class SecretsManagerLocatorTests
{
    [Fact]
    public void Finds_exe_under_secrets_manager_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-sm-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, SecretsManagerLocator.BundleFolderName);
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, SecretsManagerLocator.ExeName);
        File.WriteAllBytes(exe, new byte[] { 0x4D, 0x5A });
        try
        {
            var found = SecretsManagerLocator.FindExePath(root);
            Assert.NotNull(found);
            Assert.Equal(Path.GetFullPath(exe), Path.GetFullPath(found!));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Finds_exe_under_agent_secrets_manager_alternate_layout()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-sm-agent-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, "agent", SecretsManagerLocator.BundleFolderName);
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, SecretsManagerLocator.ExeName);
        File.WriteAllBytes(exe, new byte[] { 0x4D, 0x5A });
        try
        {
            var found = SecretsManagerLocator.FindExePath(root);
            Assert.NotNull(found);
            Assert.Equal(Path.GetFullPath(exe), Path.GetFullPath(found!));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Missing_exe_returns_null()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-sm-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Null(SecretsManagerLocator.FindExePath(root));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Env_override_wins_when_no_base_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-sm-env-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var exe = Path.Combine(root, "custom-" + SecretsManagerLocator.ExeName);
        File.WriteAllBytes(exe, new byte[] { 0x4D, 0x5A });
        var prev = Environment.GetEnvironmentVariable(SecretsManagerLocator.EnvPath);
        try
        {
            Environment.SetEnvironmentVariable(SecretsManagerLocator.EnvPath, exe);
            var found = SecretsManagerLocator.FindExePath();
            Assert.NotNull(found);
            Assert.Equal(Path.GetFullPath(exe), Path.GetFullPath(found!));
        }
        finally
        {
            Environment.SetEnvironmentVariable(SecretsManagerLocator.EnvPath, prev);
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }
}
