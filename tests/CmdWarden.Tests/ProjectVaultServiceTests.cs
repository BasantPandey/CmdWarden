using System.Net;
using System.Net.Http;
using System.Text;
using CmdWarden.Agent;
using CmdWarden.Agent.Approval;
using CmdWarden.Agent.Identity;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Grpc;
using Google.Protobuf;
using Grpc.Core;

namespace CmdWarden.Tests;

public class ProjectVaultServiceTests
{
    [Fact]
    public async Task Save_keeps_a_global_value_and_a_project_value()
    {
        var root = NewRoot();
        try
        {
            var vault = new MemoryVault();
            var service = Service(root, vault);
            var global = await service.SaveSecret(Secret("TOKEN", "global"), null!);
            Assert.Equal(VaultNames.TargetName("TOKEN"), global.TargetName);
            Assert.Equal("global", Text(vault.Read("TOKEN")));

            var folder = Path.Combine(root, "repo");
            var saved = await service.SaveSecret(Secret("TOKEN", "local", folder), null!);
            var target = VaultNames.ProjectTarget("TOKEN", VaultNames.NormalizeFolder(folder)!);
            Assert.Equal(target, saved.TargetName);
            Assert.Equal("local", Text(vault.ReadTarget(target)!.Blob));
            Assert.Equal(VaultNames.NormalizeFolder(folder), vault.LastComment);

            var longFolder = Path.GetFullPath(Path.Combine(root, new string('a', 180), new string('b', 180)));
            await service.SaveSecret(Secret("TOKEN", "long", longFolder), null!);
            Assert.Equal(256, vault.LastComment!.Length);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Save_and_delete_reject_a_bad_name_or_a_bad_folder()
    {
        var root = NewRoot();
        try
        {
            var service = Service(root, new MemoryVault());
            var save = await Assert.ThrowsAsync<RpcException>(() => service.SaveSecret(Secret("bad name", "x"), null!));
            var delete = await Assert.ThrowsAsync<RpcException>(() =>
                service.DeleteSecret(new DeleteSecretRequest { Name = "TOKEN", ProjectPath = "bad\0" }, null!));
            Assert.Equal(StatusCode.InvalidArgument, save.StatusCode);
            Assert.Equal(StatusCode.InvalidArgument, delete.StatusCode);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Delete_removes_the_global_value_or_only_the_project_value()
    {
        var root = NewRoot();
        try
        {
            var vault = new MemoryVault();
            var service = Service(root, vault);
            vault.Put("TOKEN", "global");
            var folder = Path.Combine(root, "repo");
            var target = VaultNames.ProjectTarget("TOKEN", VaultNames.NormalizeFolder(folder)!);
            vault.PutTarget(target, "local");

            var project = await service.DeleteSecret(new DeleteSecretRequest { Name = "TOKEN", ProjectPath = folder }, null!);
            Assert.True(project.Deleted);
            Assert.Null(vault.ReadTarget(target));
            Assert.Equal("global", Text(vault.Read("TOKEN")));

            var global = await service.DeleteSecret(new DeleteSecretRequest { Name = "TOKEN" }, null!);
            Assert.True(global.Deleted);
            Assert.False((await service.DeleteSecret(new DeleteSecretRequest { Name = "TOKEN" }, null!)).Deleted);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Release_returns_the_project_value_and_an_extra_name()
    {
        var root = NewRoot();
        try
        {
            var vault = new MemoryVault();
            var folder = Path.Combine(root, "repo");
            Directory.CreateDirectory(folder);
            var target = VaultNames.ProjectTarget("TOKEN", VaultNames.NormalizeFolder(folder)!);
            vault.PutTarget(target, "local");
            vault.Put("TOKEN", "global");
            vault.Put("OTHER", "extra");
            var service = Service(root, vault);

            var request = new ReleaseSecretRequest
            {
                Name = "TOKEN",
                WorkingDirectory = folder,
                Tool = "inject",
                CommandClass = "read",
            };
            request.ExtraNames.Add("OTHER");
            var released = await service.ReleaseSecret(request, null!);
            Assert.Equal("local", released.Value.ToStringUtf8());
            Assert.Equal(target, released.TargetName);
            Assert.Equal("extra", released.ExtraValues["OTHER"].ToStringUtf8());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Release_says_the_name_is_missing()
    {
        var root = NewRoot();
        try
        {
            var service = Service(root, new MemoryVault());
            var missing = await Assert.ThrowsAsync<RpcException>(() =>
                service.ReleaseSecret(new ReleaseSecretRequest { Name = "NOPE", CommandClass = "read" }, null!));
            Assert.Equal(StatusCode.NotFound, missing.StatusCode);

            var vault = new MemoryVault();
            vault.Put("TOKEN", "global");
            vault.Put("OTHER", "extra");
            vault.DropAfterRead = "OTHER";
            var dropped = Service(root, vault);
            var request = new ReleaseSecretRequest { Name = "TOKEN", CommandClass = "read", Tool = "inject" };
            request.ExtraNames.Add("OTHER");
            var gone = await Assert.ThrowsAsync<RpcException>(() => dropped.ReleaseSecret(request, null!));
            Assert.Equal(StatusCode.NotFound, gone.StatusCode);

            var main = new MemoryVault();
            main.Put("TOKEN", "global");
            main.DropAfterRead = "TOKEN";
            var lost = await Assert.ThrowsAsync<RpcException>(() =>
                Service(root, main).ReleaseSecret(new ReleaseSecretRequest { Name = "TOKEN", CommandClass = "read" }, null!));
            Assert.Equal(StatusCode.NotFound, lost.StatusCode);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Authorize_aws_keeps_the_long_term_key_for_an_sts_token_command()
    {
        var root = NewRoot();
        try
        {
            var vault = new MemoryVault();
            vault.Put("AWS_ACCESS_KEY_ID", "AKIAEXAMPLE");
            vault.Put("AWS_SECRET_ACCESS_KEY", "long-term-secret");
            var service = Service(root, vault);
            var response = await service.Authorize(Command("aws", "sts", "get-session-token"), null!);
            Assert.True(response.Allowed);
            Assert.Equal("AKIAEXAMPLE", response.Env["AWS_ACCESS_KEY_ID"]);
            Assert.Equal("long-term-secret", response.Env["AWS_SECRET_ACCESS_KEY"]);
            Assert.False(response.Env.ContainsKey("AWS_SESSION_TOKEN"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Authorize_aws_reports_a_failed_session_mint()
    {
        var root = NewRoot();
        SessionAgentService.AwsSendForTests = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("no"),
        });
        try
        {
            var vault = new MemoryVault();
            vault.Put("AWS_ACCESS_KEY_ID", "AKIAEXAMPLE");
            vault.Put("AWS_SECRET_ACCESS_KEY", "long-term-secret");
            var service = Service(root, vault);
            var failed = await Assert.ThrowsAsync<RpcException>(() => service.Authorize(Command("aws", "s3", "ls"), null!));
            Assert.Equal(StatusCode.FailedPrecondition, failed.StatusCode);
            Assert.Equal("AWS session mint failed.", failed.Status.Detail);
        }
        finally
        {
            SessionAgentService.AwsSendForTests = null;
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Authorize_aws_says_a_pack_secret_is_missing_when_it_disappears()
    {
        var root = NewRoot();
        try
        {
            var vault = new MemoryVault();
            vault.Put("AWS_ACCESS_KEY_ID", "AKIAEXAMPLE");
            vault.Put("AWS_SECRET_ACCESS_KEY", "long-term-secret");
            vault.DropAfterRead = "AWS_SECRET_ACCESS_KEY";
            var service = Service(root, vault);
            var missing = await Assert.ThrowsAsync<RpcException>(() =>
                service.Authorize(Command("aws", "sts", "get-session-token"), null!));
            Assert.Equal(StatusCode.NotFound, missing.StatusCode);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Authorize_gh_puts_the_token_in_the_child_env()
    {
        var root = NewRoot();
        try
        {
            var vault = new MemoryVault();
            vault.Put("GH_TOKEN", "ghp_example");
            var service = Service(root, vault);
            var response = await service.Authorize(Command("gh", "issue", "list"), null!);
            Assert.Equal("ghp_example", response.Env["GH_TOKEN"]);

            var missing = await Assert.ThrowsAsync<RpcException>(() =>
                Service(root, new MemoryVault()).Authorize(Command("gh", "issue", "list"), null!));
            Assert.Equal(StatusCode.NotFound, missing.StatusCode);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Authorize_docker_adds_the_auth_config_only_when_it_is_present()
    {
        var root = NewRoot();
        try
        {
            var present = new MemoryVault();
            present.Put("DOCKER_AUTH_CONFIG", "{\"auths\":{}}");
            var withAuth = await Service(root, present).Authorize(Command("docker", "ps"), null!);
            Assert.Equal("{\"auths\":{}}", withAuth.Env["DOCKER_AUTH_CONFIG"]);

            var without = await Service(root, new MemoryVault()).Authorize(Command("docker", "ps"), null!);
            Assert.False(without.Env.ContainsKey("DOCKER_AUTH_CONFIG"));
            Assert.True(without.Allowed);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static SaveSecretRequest Secret(string name, string value, string? project = null) => new()
    {
        Name = name,
        Value = ByteString.CopyFromUtf8(value),
        ProjectPath = project ?? "",
    };

    private static AuthorizeRequest Command(string tool, params string[] argv)
    {
        var request = new AuthorizeRequest { Tool = tool };
        request.Argv.AddRange(argv);
        return request;
    }

    private static SessionAgentService Service(string root, ICredentialVault vault)
    {
        var pins = new ToolPinStore(root);
        var exe = Path.Combine(root, "tool.bin");
        File.WriteAllBytes(exe, [1, 2, 3, 4]);
        foreach (var tool in new[] { "aws", "gh", "docker" })
            pins.Save(tool, exe);
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
        var root = Path.Combine(Path.GetTempPath(), "cw-vault-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    private sealed class MemoryVault : ICredentialVault
    {
        private readonly Dictionary<string, byte[]> _named = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> _targets = new(StringComparer.OrdinalIgnoreCase);

        public string? LastComment { get; private set; }

        public string? DropAfterRead { get; set; }

        public void Put(string name, string value) => _named[name] = Encoding.UTF8.GetBytes(value);

        public void PutTarget(string target, string value) => _targets[target] = Encoding.UTF8.GetBytes(value);

        public void Save(string secretName, ReadOnlySpan<byte> value) => _named[secretName] = value.ToArray();

        public void SaveTarget(string target, string? userName, ReadOnlySpan<byte> value, string? comment = null, string? label = null)
        {
            LastComment = comment;
            _targets[target] = value.ToArray();
        }

        public byte[] Read(string secretName)
        {
            if (!_named.TryGetValue(secretName, out var bytes))
                throw new KeyNotFoundException(secretName);
            var copy = bytes.ToArray();
            if (secretName.Equals(DropAfterRead, StringComparison.OrdinalIgnoreCase))
                _named.Remove(secretName);
            return copy;
        }

        public VaultEntry? ReadTarget(string target) =>
            _targets.TryGetValue(target, out var bytes) ? new VaultEntry("user", bytes.ToArray(), LastComment ?? "") : null;

        public string[] ListNames() => _named.Keys.ToArray();

        public IReadOnlyList<VaultTarget> ListTargets(string prefix) =>
            _targets.Keys.Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(key => new VaultTarget(key, "user"))
                .ToArray();

        public bool Delete(string secretName) => _named.Remove(secretName);

        public bool DeleteTarget(string target) => _targets.Remove(target);
    }
}
