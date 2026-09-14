using System.Diagnostics;
using System.Net;
using System.Text;
using CmdWarden.Agent.Identity;
using CmdWarden.Cli;
using CmdWarden.Cli.Harden;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// git-credential-cmdwarden process seam (#206). The test host's parent stands in
/// for the pinned git.exe so the signer walk passes at depth 1.
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class GitCredentialHelperProcessTests
{
    private static string NewHost() => "https://git-helper-" + Guid.NewGuid().ToString("N")[..8] + ".example.test";

    private static string? PinParentAsGit(string productRoot)
    {
        var chain = new ProcessChainWalker().Walk(Environment.ProcessId);
        var parent = chain.Count > 1 ? chain[1].Path : null;
        if (parent is null)
            return null;
        new ToolPinStore(productRoot).Save("git", parent);
        return parent;
    }

    private static string InstallHelperIntoShims(string productRoot)
    {
        var shims = Path.Combine(productRoot, "shims");
        GitHarden.InstallShimPayload(TestPaths.FindGitShimOutputDir(), shims);
        GitHarden.InstallShimPayload(TestPaths.FindGitHelperOutputDir(), shims);
        var helper = Path.Combine(shims, HelperTools.GitHelperExe);
        Assert.True(File.Exists(helper));
        return helper;
    }

    private static string GitConfigWithHelper(string helperExe)
    {
        var path = Path.Combine(Path.GetTempPath(), "cw-gitconfig-" + Guid.NewGuid().ToString("N"));
        var escaped = helperExe.Replace('\\', '/');
        if (escaped.Contains(' ', StringComparison.Ordinal))
            escaped = escaped.Replace(" ", "\\ ", StringComparison.Ordinal);
        File.WriteAllText(path, "[credential]\n\thelper =\n\thelper = " + escaped + "\n");
        return path;
    }

    private static void DeleteGit(string serverUrl, params string[] usernames)
    {
        var vault = new CredentialVault();
        var names = usernames.Length == 0 ? new[] { "" } : usernames;
        foreach (var user in names)
        {
            var ctx = GitVaultNames.Parse(serverUrl, user);
            vault.DeleteTarget(GitVaultNames.Target(ctx.HostKey));
            vault.DeleteTarget(GitVaultNames.Target(ctx.AccountKey));
            vault.DeleteTarget(GitVaultNames.Target(ctx.RefreshKey));
        }
    }

    private static async Task<(int Exit, string Stdout, string Stderr)> RunHelperAsync(
        string action,
        string stdin,
        string? pipeName)
    {
        var stdout = new StringWriter { NewLine = "\n" };
        var stderr = new StringWriter { NewLine = "\n" };
        var exit = await GitCredentialHelperApp.RunAsync(
            action.Length == 0 ? Array.Empty<string>() : new[] { action },
            new StringReader(stdin),
            stdout,
            stderr,
            pipeName,
            TimeSpan.FromSeconds(30));
        return (exit, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public async Task Get_prints_username_then_password()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "off", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        Assert.NotNull(PinParentAsGit(fx.ProductRoot));

        var host = NewHost();
        var secret = "tok-" + Guid.NewGuid().ToString("N");
        try
        {
            new CredentialVault().SaveTarget(
                GitVaultNames.Target(host), "alice", CredentialVault.Utf8Bytes(secret));

            var (exit, stdout, _) = await RunHelperAsync(
                "get",
                "capability[]=authtype\nprotocol=https\nhost=" + host["https://".Length..] + "\nusername=alice\n\n",
                fx.PipeName);

            Assert.Equal(0, exit);
            Assert.Contains("capability[]=authtype\n", stdout, StringComparison.Ordinal);
            var userAt = stdout.IndexOf("username=", StringComparison.Ordinal);
            var passAt = stdout.IndexOf("password=", StringComparison.Ordinal);
            Assert.True(userAt >= 0 && passAt > userAt);
            Assert.Contains("username=alice\n", stdout, StringComparison.Ordinal);
            Assert.Contains("password=" + secret + "\n", stdout, StringComparison.Ordinal);
        }
        finally
        {
            DeleteGit(host, "alice", "");
        }
    }

    [Fact]
    public async Task Not_found_prints_nothing_on_exit_0()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "off", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        Assert.NotNull(PinParentAsGit(fx.ProductRoot));

        var (exit, stdout, stderr) = await RunHelperAsync(
            "get",
            "protocol=https\nhost=missing-" + Guid.NewGuid().ToString("N")[..8] + ".example.test\n\n",
            fx.PipeName);

        Assert.Equal(0, exit);
        Assert.Equal("", stdout);
        Assert.Equal("", stderr);
    }

    [Fact]
    public async Task Deny_prints_quit_true_and_stderr_line()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "off", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        new ToolPinStore(fx.ProductRoot).Save("git", Path.Combine(Environment.SystemDirectory, "cmd.exe"));

        var host = "https://deny-" + Guid.NewGuid().ToString("N")[..8] + ".example.test";
        var (exit, stdout, stderr) = await RunHelperAsync(
            "get",
            "protocol=https\nhost=" + host["https://".Length..] + "\n\n",
            fx.PipeName);

        Assert.Equal(0, exit);
        Assert.Equal("quit=true\n", stdout);
        Assert.Contains("CmdWarden: git credential denied for " + host, stderr, StringComparison.Ordinal);
        Assert.Contains(PolicyReasonCodes.HelperParentMissing, stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Agent_down_prints_quit_true_and_AgentDown_reason()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var noAgent = new NoAgentBinary();
        var missingPipe = $"{AgentEndpoints.PipeNamePrefix}-missing-helper-{Guid.NewGuid():N}";
        var host = "https://down-" + Guid.NewGuid().ToString("N")[..8] + ".example.test";
        var stdout = new StringWriter { NewLine = "\n" };
        var stderr = new StringWriter { NewLine = "\n" };
        var exit = await GitCredentialHelperApp.RunAsync(
            new[] { "get" },
            new StringReader("protocol=https\nhost=" + host["https://".Length..] + "\n\n"),
            stdout,
            stderr,
            missingPipe,
            TimeSpan.FromMilliseconds(800));

        Assert.Equal(0, exit);
        Assert.Equal("quit=true\n", stdout.ToString());
        Assert.Contains(
            GitCredentialProtocol.DenyStderr(host, PolicyReasonCodes.AgentDown),
            stderr.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Store_and_erase_pass_the_secret_and_print_nothing()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "off", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        Assert.NotNull(PinParentAsGit(fx.ProductRoot));

        var host = NewHost();
        var secret = "st-" + Guid.NewGuid().ToString("N");
        var body = "protocol=https\nhost=" + host["https://".Length..] + "\nusername=alice\npassword=" + secret + "\n\n";
        try
        {
            var stored = await RunHelperAsync("store", body, fx.PipeName);
            Assert.Equal(0, stored.Exit);
            Assert.Equal("", stored.Stdout);

            var got = await AgentHelperClient.CredentialAsync(
                "git", "get", host, username: "alice", pipeName: fx.PipeName);
            Assert.Equal(secret, got.Secret);

            var erased = await RunHelperAsync("erase", body, fx.PipeName);
            Assert.Equal(0, erased.Exit);
            Assert.Equal("", erased.Stdout);

            var missing = await RunHelperAsync("get",
                "protocol=https\nhost=" + host["https://".Length..] + "\nusername=alice\n\n",
                fx.PipeName);
            Assert.Equal(0, missing.Exit);
            Assert.Equal("", missing.Stdout);
        }
        finally
        {
            DeleteGit(host, "alice");
        }
    }

    [Fact]
    public async Task Unknown_action_is_ignored_with_exit_0()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var (exit, stdout, stderr) = await RunHelperAsync("version", "", pipeName: "unused");
        Assert.Equal(0, exit);
        Assert.Equal("", stdout);
        Assert.Equal("", stderr);
    }

    [Fact]
    public async Task Git_credential_fill_returns_the_vaulted_token()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var git = FindRealGit();
        if (git is null)
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "off", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        new ToolPinStore(fx.ProductRoot).Save("git", git);
        var helper = InstallHelperIntoShims(fx.ProductRoot);
        var gitconfig = GitConfigWithHelper(helper);

        var host = "git-fill-" + Guid.NewGuid().ToString("N")[..8] + ".example.test";
        var url = "https://" + host;
        var secret = "fill-" + Guid.NewGuid().ToString("N");
        try
        {
            new CredentialVault().SaveTarget(
                GitVaultNames.Target(url), "alice", CredentialVault.Utf8Bytes(secret));

            var psi = new ProcessStartInfo
            {
                FileName = git,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("credential");
            psi.ArgumentList.Add("fill");
            psi.Environment["CW_PIPE_NAME"] = fx.PipeName;
            psi.Environment[ProductPaths.EnvVar] = fx.ProductRoot;
            psi.Environment["GIT_CONFIG_GLOBAL"] = gitconfig;
            psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            psi.Environment["GCM_INTERACTIVE"] = "never";

            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("git failed to start");
            await proc.StandardInput.WriteAsync("protocol=https\nhost=" + host + "\n\n");
            proc.StandardInput.Close();
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            Assert.True(proc.ExitCode == 0, stderr);
            Assert.Contains("username=alice", stdout, StringComparison.Ordinal);
            Assert.Contains("password=" + secret, stdout, StringComparison.Ordinal);
        }
        finally
        {
            DeleteGit(url, "alice", "");
            try { File.Delete(gitconfig); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Git_ls_remote_through_the_shim_uses_the_helper_twice_without_a_prompt()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var git = FindRealGit();
        if (git is null)
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "off", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        new ToolPinStore(fx.ProductRoot).Save("git", git);
        var helper = InstallHelperIntoShims(fx.ProductRoot);
        var shimGit = Path.Combine(fx.ProductRoot, "shims", "git.exe");
        var gitconfig = GitConfigWithHelper(helper);

        var secret = "http-" + Guid.NewGuid().ToString("N");
        using var server = new GitHttpBasicServer("alice", secret);
        var url = server.RepoUrl;
        var hostUrl = "http://" + server.Host;
        try
        {
            new CredentialVault().SaveTarget(
                GitVaultNames.Target(hostUrl), "alice", CredentialVault.Utf8Bytes(secret));

            async Task<int> LsRemoteAsync()
            {
                var psi = new ProcessStartInfo
                {
                    FileName = shimGit,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add("ls-remote");
                psi.ArgumentList.Add(url);
                psi.Environment["CW_PIPE_NAME"] = fx.PipeName;
                psi.Environment[ProductPaths.EnvVar] = fx.ProductRoot;
                psi.Environment["GIT_CONFIG_GLOBAL"] = gitconfig;
                psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
                psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
                psi.Environment["GCM_INTERACTIVE"] = "never";
                using var proc = Process.Start(psi) ?? throw new InvalidOperationException("shim git failed to start");
                var stderr = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();
                Assert.True(proc.ExitCode == 0, stderr);
                return proc.ExitCode;
            }

            Assert.Equal(0, await LsRemoteAsync());
            Assert.Equal(0, await LsRemoteAsync());
            Assert.True(server.AuthenticatedGets >= 2);
        }
        finally
        {
            DeleteGit(hostUrl, "alice", "");
            try { File.Delete(gitconfig); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Git_credential_fill_through_the_shim_prompts_under_Trusted()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var git = FindRealGit();
        if (git is null)
            return;

        await using var fx = await ApprovalMemoryFixture.CreateAsync(
            approvalMode: "off", enroll: LauncherEnrollmentKind.Terminal);
        if (fx is null)
            return;
        new ToolPinStore(fx.ProductRoot).Save("git", git);

        var ex = await Assert.ThrowsAsync<Grpc.Core.RpcException>(() =>
            AgentAuthorizeClient.AuthorizeAsync("git", new[] { "credential", "fill" }, pipeName: fx.PipeName));
        Assert.Equal(Grpc.Core.StatusCode.PermissionDenied, ex.StatusCode);
        Assert.Contains("secret-reveal", ex.Status.Detail, StringComparison.Ordinal);
    }

    private static string? FindRealGit()
    {
        var fromDiscover = GitDiscoverer.FindRealGit();
        if (fromDiscover is not null && File.Exists(fromDiscover))
            return fromDiscover;
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe");
        return File.Exists(fallback) ? fallback : null;
    }

    /// <summary>Loopback git-upload-pack advertisement behind Basic auth.</summary>
    private sealed class GitHttpBasicServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly string _expectedAuth;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;

        public GitHttpBasicServer(string user, string password)
        {
            _expectedAuth = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password));
            var port = FreePort();
            Host = "127.0.0.1:" + port;
            RepoUrl = "http://" + Host + "/repo.git";
            _listener.Prefixes.Add("http://" + Host + "/");
            _listener.Start();
            _loop = Task.Run(ListenAsync);
        }

        public string Host { get; }
        public string RepoUrl { get; }
        public int AuthenticatedGets { get; private set; }

        private async Task ListenAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync().WaitAsync(_cts.Token);
                }
                catch (Exception)
                {
                    return;
                }

                var auth = ctx.Request.Headers["Authorization"] ?? "";
                if (!string.Equals(auth, _expectedAuth, StringComparison.Ordinal))
                {
                    ctx.Response.StatusCode = 401;
                    ctx.Response.Headers["WWW-Authenticate"] = "Basic realm=\"git\"";
                    ctx.Response.Close();
                    continue;
                }

                AuthenticatedGets++;
                var body = GitUploadPackAdvertisement();
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/x-git-upload-pack-advertisement";
                ctx.Response.ContentLength64 = body.Length;
                await ctx.Response.OutputStream.WriteAsync(body);
                ctx.Response.Close();
            }
        }

        private static byte[] GitUploadPackAdvertisement()
        {
            var head = Pkt("# service=git-upload-pack\n");
            var flush = "0000"u8.ToArray();
            var refs = Pkt("0123456789abcdef0123456789abcdef01234567 refs/heads/main\n");
            var result = new byte[head.Length + flush.Length + refs.Length + flush.Length];
            head.CopyTo(result, 0);
            flush.CopyTo(result, head.Length);
            refs.CopyTo(result, head.Length + flush.Length);
            flush.CopyTo(result, head.Length + flush.Length + refs.Length);
            return result;
        }

        private static byte[] Pkt(string text)
        {
            var payload = Encoding.UTF8.GetBytes(text);
            var prefix = Encoding.ASCII.GetBytes($"{payload.Length + 4:x4}");
            var line = new byte[prefix.Length + payload.Length];
            prefix.CopyTo(line, 0);
            payload.CopyTo(line, prefix.Length);
            return line;
        }

        private static int FreePort()
        {
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { /* ignore */ }
            _listener.Close();
            _cts.Dispose();
        }
    }
}
