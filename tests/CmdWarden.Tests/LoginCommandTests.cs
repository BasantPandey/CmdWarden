using System.Text;
using CmdWarden.Agent;
using CmdWarden.Agent.Approval;
using CmdWarden.Agent.Identity;
using CmdWarden.Cli;
using CmdWarden.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CmdWarden.Tests;

public class LoginCommandTests
{
    [Fact]
    public async Task Help_names_the_login_flags_and_the_list_command()
    {
        var help = await CliOutput.Capture(() => CliApp.RunAsync(["--help"]));
        Assert.Equal(0, help.Code);
        Assert.Contains("--user", help.Out, StringComparison.Ordinal);
        Assert.Contains("--site", help.Out, StringComparison.Ordinal);
        Assert.Contains("login list", help.Out, StringComparison.Ordinal);

        var save = await CliOutput.Capture(() => CliApp.RunAsync(["save", "--help"]));
        Assert.Equal(0, save.Code);
        Assert.Contains("--user <account> --site <host>", save.Out, StringComparison.Ordinal);

        var login = await CliOutput.Capture(() => CliApp.RunAsync(["login", "--help"]));
        Assert.Equal(0, login.Code);
        Assert.Contains("cw login list", login.Out, StringComparison.Ordinal);

        var doc = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "docs", "cli-reference.md"));
        Assert.Contains("`cw save <NAME> --user <account> --site <host>`", doc, StringComparison.Ordinal);
        Assert.Contains("`cw login list`", doc, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_rejects_a_half_login_and_a_bad_site_before_the_agent()
    {
        var half = await CliOutput.Capture(() => CliApp.RunAsync(["save", "GITHUB", "--user", "ada", "--value", "secret"]));
        Assert.Equal(1, half.Code);
        Assert.Contains("Site list is required.", half.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("Session Agent", half.Err, StringComparison.Ordinal);

        var space = await CliOutput.Capture(() => CliApp.RunAsync(["save", "GITHUB", "--user", "ada", "--site", "bad host", "--value", "secret"]));
        Assert.Equal(1, space.Code);
        Assert.Contains("Site host has a character that is not allowed.", space.Err, StringComparison.Ordinal);

        var url = await CliOutput.Capture(() => CliApp.RunAsync(["save", "GITHUB", "--user", "ada", "--site", "https://github.com", "--value", "secret"]));
        Assert.Equal(1, url.Code);
        Assert.Contains("Site host has a character that is not allowed.", url.Err, StringComparison.Ordinal);

        var project = await CliOutput.Capture(() => CliApp.RunAsync(
            ["save", "GITHUB", "--user", "ada", "--site", "github.com", "--project", @"C:\repo", "--value", "secret"]));
        Assert.Equal(1, project.Code);
        Assert.Contains("A login does not take a project folder.", project.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("Session Agent", project.Err, StringComparison.Ordinal);

        var unknown = await CliOutput.Capture(() => CliApp.RunAsync(["login", "reveal"]));
        Assert.Equal(1, unknown.Code);
        Assert.Contains("Unknown command: login reveal", unknown.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("Session Agent", unknown.Err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_stores_the_login_and_the_list_hides_the_password()
    {
        await using var host = await LoginPipe.Start();
        var saved = await host.Run("save", "GITHUB", "--user", "ada", "--site", "github.com", "--value", "THE-PASSWORD");
        Assert.Equal(0, saved.Code);
        Assert.Equal("THE-PASSWORD", host.Vault.Text("GITHUB"));
        Assert.DoesNotContain("THE-PASSWORD", saved.Out, StringComparison.Ordinal);

        var plain = await host.Run("save", "TOKEN", "--value", "global-secret");
        Assert.Equal(0, plain.Code);

        var list = await host.Run("login", "list");
        Assert.Equal(0, list.Code);
        Assert.Contains("GITHUB ada github.com", list.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("THE-PASSWORD", list.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("global-secret", list.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("TOKEN", list.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generate_hides_the_password_and_an_empty_list_says_none()
    {
        await using var host = await LoginPipe.Start();
        var empty = await host.Run("login", "list");
        Assert.Equal(0, empty.Code);
        Assert.Contains("No login is stored.", empty.Out, StringComparison.Ordinal);

        var saved = await host.Run("save", "GITHUB", "--generate", "--user", "ada", "--site", "github.com");
        Assert.Equal(0, saved.Code);
        var password = host.Vault.Text("GITHUB");
        Assert.Equal(PasswordGenerator.DefaultLength, password.Length);
        Assert.DoesNotContain(password, saved.Out, StringComparison.Ordinal);

        var list = await host.Run("login", "list");
        Assert.Contains("GITHUB ada github.com", list.Out, StringComparison.Ordinal);
        Assert.DoesNotContain(password, list.Out, StringComparison.Ordinal);
    }

    private sealed class LoginPipe : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly string? _oldPipe;

        private LoginPipe(WebApplication app, MemoryVault vault, string pipe, string? oldPipe)
        {
            _app = app;
            Vault = vault;
            Pipe = pipe;
            _oldPipe = oldPipe;
            Environment.SetEnvironmentVariable("CW_PIPE_NAME", pipe);
        }

        public MemoryVault Vault { get; }

        public string Pipe { get; }

        public static async Task<LoginPipe> Start()
        {
            var root = Path.Combine(Path.GetTempPath(), "cw-login-cmd-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var pipe = AgentEndpoints.PipeNamePrefix + "-login-" + Guid.NewGuid().ToString("N");
            var vault = new MemoryVault();
            var app = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ContentRootPath = root }).BuildHost(pipe, root, vault);
            await app.StartAsync();
            var health = await AgentHealthClient.GetHealthAsync(pipe, TimeSpan.FromSeconds(15));
            Assert.True(health.Alive);
            return new LoginPipe(app, vault, pipe, Environment.GetEnvironmentVariable("CW_PIPE_NAME"));
        }

        public Task<(int Code, string Out, string Err)> Run(params string[] args) =>
            CliOutput.Capture(() => CliApp.RunAsync(args));

        public async ValueTask DisposeAsync()
        {
            Environment.SetEnvironmentVariable("CW_PIPE_NAME", _oldPipe);
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
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

file static class LoginHostBuilder
{
    public static WebApplication BuildHost(this WebApplicationBuilder builder, string pipe, string root, ICredentialVault vault)
    {
        builder.Logging.ClearProviders();
        builder.WebHost.UseNamedPipes(options => options.CurrentUserOnly = true);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenNamedPipe(pipe, listen => listen.Protocols = HttpProtocols.Http2);
        });
        var pins = new ToolPinStore(root);
        File.WriteAllBytes(Path.Combine(root, "tool.bin"), [1, 2, 3, 4]);
        pins.Save("gh", Path.Combine(root, "tool.bin"));
        var service = new SessionAgentService(
            new AgentRuntimeInfo(pipe, Path.Combine(root, "policy.json"), root),
            vault,
            new LauncherIdentityResolver(pins),
            new PolicyStore(Path.Combine(root, "policy.json")),
            new ScriptedApprovalGate(ApprovalOutcome.AllowOnce),
            new ApprovalMemory(),
            pins,
            new AuditLog(root),
            new AlarmNotifier(false));
        builder.Services.AddGrpc();
        builder.Services.AddSingleton(service);
        var app = builder.Build();
        app.MapGrpcService<SessionAgentService>();
        return app;
    }
}
