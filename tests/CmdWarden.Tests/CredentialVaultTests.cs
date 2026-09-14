using CmdWarden.Contracts;

namespace CmdWarden.Tests;

public class CredentialVaultTests
{
    [Fact]
    public void Save_Read_Delete_round_trip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var name = "cw_test_" + Guid.NewGuid().ToString("N")[..12];
        var vault = new CredentialVault();
        var payload = "s3cret-value-" + Guid.NewGuid().ToString("N");

        try
        {
            vault.Save(name, System.Text.Encoding.UTF8.GetBytes(payload));
            var read = vault.Read(name);
            Assert.Equal(payload, System.Text.Encoding.UTF8.GetString(read));
        }
        finally
        {
            vault.Delete(name);
        }

        Assert.Throws<KeyNotFoundException>(() => vault.Read(name));
    }

    [Fact]
    public void ListNames_returns_saved_names_and_reflects_delete()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // Asserts Contains/DoesNotContain rather than an exact set: this class isn't in the
        // AgentProcess collection, so it can run concurrently with other tests writing to the
        // same real, global Windows Credential Manager.
        var vault = new CredentialVault();
        var name1 = "cw_list_" + Guid.NewGuid().ToString("N")[..12];
        var name2 = "cw_list_" + Guid.NewGuid().ToString("N")[..12];

        try
        {
            vault.Save(name1, System.Text.Encoding.UTF8.GetBytes("v1"));
            vault.Save(name2, System.Text.Encoding.UTF8.GetBytes("v2"));

            var names = vault.ListNames();
            Assert.Contains(name1, names);
            Assert.Contains(name2, names);

            vault.Delete(name1);
            names = vault.ListNames();
            Assert.DoesNotContain(name1, names);
            Assert.Contains(name2, names);
        }
        finally
        {
            vault.Delete(name1);
            vault.Delete(name2);
        }
    }

    [Fact]
    public void ListNames_does_not_throw_when_filter_matches_nothing()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // CredEnumerateW returns ERROR_NOT_FOUND when the filter matches zero entries --
        // must surface as an empty list, not a thrown error. Asserts non-throwing rather than
        // Assert.Empty: other concurrent tests may hold their own CmdWarden-prefixed secrets
        // in this same real, global Windows Credential Manager.
        var vault = new CredentialVault();
        var name = "cw_list_solo_" + Guid.NewGuid().ToString("N")[..8];
        vault.Save(name, System.Text.Encoding.UTF8.GetBytes("v"));
        try
        {
            vault.Delete(name);
            var exception = Record.Exception(() => vault.ListNames());
            Assert.Null(exception);
        }
        finally
        {
            vault.Delete(name);
        }
    }
}
