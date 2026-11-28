using System.Text;
using CmdWarden.Agent;
using CmdWarden.Agent.Approval;
using CmdWarden.Agent.Identity;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Grpc;
using Google.Protobuf;
using Grpc.Core;

namespace CmdWarden.Tests;

public class LoginSaveTests
{
    [Fact]
    public void Format_keeps_hosts_and_rejects_a_bad_list()
    {
        Assert.Equal("sites:github.com,login.example.com", LoginSites.Format(" github.com, login.example.com "));
        Assert.False(LoginSites.TryParse("CmdWarden vault secret", out _));
        Assert.True(LoginSites.TryParse("sites:github.com", out var sites));
        Assert.Equal("github.com", sites);

        Assert.Throws<ArgumentException>(() => LoginSites.Format(""));
        Assert.Throws<ArgumentException>(() => LoginSites.Format("bad host"));
        Assert.Throws<ArgumentException>(() => LoginSites.Format("https://github.com"));
        Assert.Throws<ArgumentException>(() => LoginSites.Format(new string('a', 251)));
        Assert.Throws<ArgumentException>(() => LoginSites.Account(""));
        Assert.Throws<ArgumentException>(() => LoginSites.Account("ada\n"));
    }

    [Fact]
    public async Task Save_stores_the_password_and_lists_the_login_without_it()
    {
        var root = NewRoot();
        try
        {
            var vault = new MemoryVault();
            var service = Service(root, vault);
            await service.SaveSecret(Secret("TOKEN", "global-secret"), null!);
            await service.SaveSecret(Secret("GITHUB", "pw-secret", account: "ada", sites: "github.com"), null!);

            var listed = await service.ListLogins(new ListLoginsRequest(), null!);
            var login = Assert.Single(listed.Logins);
            Assert.Equal("GITHUB", login.Name);
            Assert.Equal("ada", login.Account);
            Assert.Equal("github.com", login.Sites);
            Assert.DoesNotContain("pw-secret", listed.ToString(), StringComparison.Ordinal);
            Assert.Equal("pw-secret", vault.Text("GITHUB"));
            Assert.Equal("global-secret", vault.Text("TOKEN"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Save_rejects_a_project_login_and_a_half_login()
    {
        var root = NewRoot();
        try
        {
            var service = Service(root, new MemoryVault());
            var project = await Assert.ThrowsAsync<RpcException>(() =>
                service.SaveSecret(Secret("GITHUB", "pw", account: "ada", sites: "github.com", project: @"C:\repo"), null!));
            Assert.Equal(StatusCode.InvalidArgument, project.StatusCode);

            var half = await Assert.ThrowsAsync<RpcException>(() =>
                service.SaveSecret(Secret("GITHUB", "pw", sites: "github.com"), null!));
            Assert.Equal(StatusCode.InvalidArgument, half.StatusCode);

            var longList = await Assert.ThrowsAsync<RpcException>(() =>
                service.SaveSecret(Secret("GITHUB", "pw", account: "ada", sites: new string('a', 251)), null!));
            Assert.Equal(StatusCode.InvalidArgument, longList.StatusCode);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task The_client_sends_the_account_and_the_sites_before_the_call_fails()
    {
        var pipe = "cw-no-agent-" + Guid.NewGuid().ToString("N");
        var timeout = TimeSpan.FromMilliseconds(400);
        await Assert.ThrowsAnyAsync<Exception>(() =>
            AgentVaultClient.SaveAsync("GITHUB", [1], pipeName: pipe, timeout: timeout, account: "ada", sites: "github.com"));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            AgentVaultClient.ListLoginsAsync(pipe, timeout));
    }

    private static SaveSecretRequest Secret(
        string name, string value, string? account = null, string? sites = null, string? project = null) => new()
    {
        Name = name,
        Value = ByteString.CopyFromUtf8(value),
        Account = account ?? "",
        Sites = sites ?? "",
        ProjectPath = project ?? "",
    };

    private static SessionAgentService Service(string root, ICredentialVault vault)
    {
        var pins = new ToolPinStore(root);
        var exe = Path.Combine(root, "tool.bin");
        File.WriteAllBytes(exe, [1, 2, 3, 4]);
        pins.Save("gh", exe);
        return new SessionAgentService(
            new AgentRuntimeInfo("pipe", Path.Combine(root, "policy.json"), root),
            vault,
            new LauncherIdentityResolver(pins),
            new PolicyStore(Path.Combine(root, "policy.json")),
            new ScriptedApprovalGate(ApprovalOutcome.AllowOnce),
            new ApprovalMemory(),
            pins,
            new AuditLog(root),
            new AlarmNotifier(false));
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-login-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class MemoryVault : ICredentialVault
    {
        private sealed record Row(byte[] Blob, string User, string Comment);

        private readonly Dictionary<string, Row> _rows = new(StringComparer.OrdinalIgnoreCase);

        public void Save(string secretName, ReadOnlySpan<byte> value) =>
            _rows[VaultNames.TargetName(secretName)] = new Row(value.ToArray(), "windows-user", "CmdWarden vault secret");

        public void SaveTarget(string target, string? userName, ReadOnlySpan<byte> value, string? comment = null, string? label = null) =>
            _rows[target] = new Row(value.ToArray(), userName ?? "", comment ?? "");

        public byte[] Read(string secretName)
        {
            if (!_rows.TryGetValue(VaultNames.TargetName(secretName), out var row))
                throw new KeyNotFoundException(secretName);
            return row.Blob.ToArray();
        }

        public string Text(string secretName) => Encoding.UTF8.GetString(Read(secretName));

        public VaultEntry? ReadTarget(string target) =>
            _rows.TryGetValue(target, out var row) ? new VaultEntry(row.User, row.Blob.ToArray(), row.Comment) : null;

        public string[] ListNames() =>
            _rows.Keys.Where(key => key.StartsWith(VaultNames.TargetPrefix, StringComparison.Ordinal))
                .Select(VaultNames.SecretName)
                .ToArray();

        public IReadOnlyList<VaultTarget> ListTargets(string prefix) =>
            _rows.Where(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(pair => new VaultTarget(pair.Key, pair.Value.User, Comment: pair.Value.Comment))
                .ToArray();

        public bool Delete(string secretName) => _rows.Remove(VaultNames.TargetName(secretName));

        public bool DeleteTarget(string target) => _rows.Remove(target);
    }
}
