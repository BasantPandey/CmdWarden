namespace CmdWarden.Contracts;

/// <summary>The vault calls the Session Agent makes. Tests use a memory stand-in.</summary>
public interface ICredentialVault
{
    void Save(string secretName, ReadOnlySpan<byte> value);

    void SaveTarget(string target, string? userName, ReadOnlySpan<byte> value, string? comment = null, string? label = null);

    byte[] Read(string secretName);

    VaultEntry? ReadTarget(string target);

    string[] ListNames();

    IReadOnlyList<VaultTarget> ListTargets(string prefix);

    bool Delete(string secretName);

    bool DeleteTarget(string target);
}
