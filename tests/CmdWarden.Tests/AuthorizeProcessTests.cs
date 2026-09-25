using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using CmdWarden.Agent.Approval;
using CmdWarden.Cli;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Primary seam: Authorize (issue #29).
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class AuthorizeProcessTests
{
    [Fact]
    public async Task Authorize_grants_for_enrolled_Trusted_terminal_and_read_class_gh()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var pipeName = $"{AgentEndpoints.PipeNamePrefix}-auth-{Guid.NewGuid():N}";
        var productRoot = Path.Combine(Path.GetTempPath(), "cw-prod-" + Guid.NewGuid().ToString("N"));
        var policyPath = Path.Combine(productRoot, "policy.json");
        Directory.CreateDirectory(productRoot);

        var tokenValue = "ghp_test_" + Guid.NewGuid().ToString("N");
        var agentDll = TestPaths.FindAgentDll();

        // Pin cmd.exe as stand-in "gh" binary for path/hash checks (no real gh required).
        var pinTarget = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        Assert.True(File.Exists(pinTarget), "cmd.exe required for pin fixture");

        Environment.SetEnvironmentVariable("CW_PIPE_NAME", pipeName);
        Environment.SetEnvironmentVariable("CW_POLICY_PATH", policyPath);
        Environment.SetEnvironmentVariable(ProductPaths.EnvVar, productRoot);
        try
        {
            await using var agent = await AgentProcess.StartAsync(
                agentDll, pipeName, policyPath, productRoot, approvalMode: "off");

            var id = await AgentHealthClient.ResolveIdentityAsync(pipeName);
            Assert.True(id.AutoApproveEligible, "test host must be auto-approve eligible");

            var store = new PolicyStore(policyPath);
            store.Load();
            store.Enroll(id.SelectedPolicyKey, LauncherEnrollmentKind.Terminal);
            store.Save();

            var pins = new ToolPinStore(productRoot);
            pins.Save("gh", pinTarget);

            await AgentVaultClient.SaveAsync(
                SessionAgentServiceNames.GhToken,
                Encoding.UTF8.GetBytes(tokenValue),
                pipeName);

            var grant = await AgentAuthorizeClient.AuthorizeAsync(
                "gh",
                new[] { "pr", "list" },
                secretName: SessionAgentServiceNames.GhToken,
                pipeName: pipeName);

            Assert.True(grant.Allowed);
            Assert.Equal(pinTarget, grant.RealPath, ignoreCase: true);
            Assert.False(string.IsNullOrWhiteSpace(grant.RealSha256));
            Assert.Equal(CommandClassNames.Read, grant.CommandClass);
            Assert.Equal(PolicyLevelNames.Trusted, grant.PolicyLevel);
            Assert.Equal("auto-allow", grant.Decision);
            Assert.True(grant.Env.ContainsKey(SessionAgentServiceNames.GhToken));
            Assert.Equal(tokenValue, grant.Env[SessionAgentServiceNames.GhToken]);
            // #39: child-only GH_PATH so nested go-gh does not re-find the PATH shim
            Assert.True(grant.Env.ContainsKey(SessionAgentServiceNames.GhPath));
            Assert.Equal(pinTarget, grant.Env[SessionAgentServiceNames.GhPath], ignoreCase: true);
            Assert.NotEqual(tokenValue, grant.Env[SessionAgentServiceNames.GhPath]);

            // Audit line written
            var auditDir = Path.Combine(productRoot, "audit");
            Assert.True(Directory.Exists(auditDir));
            var files = Directory.GetFiles(auditDir, "gates-*.ndjson");
            Assert.NotEmpty(files);
            var text = File.ReadAllText(files[0]);
            Assert.Contains("auto-allow", text, StringComparison.Ordinal);
            Assert.Contains("\"tool\":\"gh\"", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(tokenValue, text);

            // cw audit seam: reader sees decision without secret value
            var auditLog = new AuditLog(productRoot);
            var recent = auditLog.ReadRecentLines(5);
            Assert.NotEmpty(recent);
            var pretty = AuditFormatter.FormatLine(recent[^1]);
            Assert.Contains("auto-allow", pretty, StringComparison.Ordinal);
            Assert.DoesNotContain(tokenValue, pretty);
        }
        finally
        {
            try
            {
                await AgentVaultClient.DeleteAsync(SessionAgentServiceNames.GhToken, pipeName);
            }
            catch { /* ignore */ }

            Environment.SetEnvironmentVariable("CW_PIPE_NAME", null);
            Environment.SetEnvironmentVariable("CW_POLICY_PATH", null);
            Environment.SetEnvironmentVariable(ProductPaths.EnvVar, null);
            try { Directory.Delete(productRoot, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Authorize_denies_when_pin_missing()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var pipeName = $"{AgentEndpoints.PipeNamePrefix}-nopin-{Guid.NewGuid():N}";
        var productRoot = Path.Combine(Path.GetTempPath(), "cw-prod-" + Guid.NewGuid().ToString("N"));
        var policyPath = Path.Combine(productRoot, "policy.json");
        Directory.CreateDirectory(productRoot);
        var agentDll = TestPaths.FindAgentDll();

        Environment.SetEnvironmentVariable("CW_PIPE_NAME", pipeName);
        Environment.SetEnvironmentVariable("CW_POLICY_PATH", policyPath);
        Environment.SetEnvironmentVariable(ProductPaths.EnvVar, productRoot);
        try
        {
            await using var agent = await AgentProcess.StartAsync(
                agentDll, pipeName, policyPath, productRoot, approvalMode: "off");

            var id = await AgentHealthClient.ResolveIdentityAsync(pipeName);
            if (!id.AutoApproveEligible)
                return;

            var store = new PolicyStore(policyPath);
            store.Load();
            store.Enroll(id.SelectedPolicyKey, LauncherEnrollmentKind.Terminal);
            store.Save();

            await AgentVaultClient.SaveAsync(
                SessionAgentServiceNames.GhToken,
                Encoding.UTF8.GetBytes("token"),
                pipeName);

            var ex = await Assert.ThrowsAsync<Grpc.Core.RpcException>(async () =>
                await AgentAuthorizeClient.AuthorizeAsync("gh", new[] { "pr", "list" }, pipeName: pipeName));

            Assert.Equal(Grpc.Core.StatusCode.FailedPrecondition, ex.StatusCode);
            Assert.Contains(PolicyReasonCodes.PinMissing, ex.Status.Detail, StringComparison.Ordinal);
        }
        finally
        {
            try { await AgentVaultClient.DeleteAsync(SessionAgentServiceNames.GhToken, pipeName); } catch { /* ignore */ }
            Environment.SetEnvironmentVariable("CW_PIPE_NAME", null);
            Environment.SetEnvironmentVariable("CW_POLICY_PATH", null);
            Environment.SetEnvironmentVariable(ProductPaths.EnvVar, null);
            try { Directory.Delete(productRoot, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Authorize_secret_reveal_not_auto_allowed_under_Read_fails_closed_when_approval_off()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await AuthorizeFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.AiHarness); // default Read
        fx.PinCmdAsGh();
        await fx.SaveTokenAsync("secret-token-value");

        var ex = await Assert.ThrowsAsync<Grpc.Core.RpcException>(async () =>
            await AgentAuthorizeClient.AuthorizeAsync(
                "gh",
                new[] { "auth", "token" },
                pipeName: fx.PipeName));

        Assert.Equal(Grpc.Core.StatusCode.PermissionDenied, ex.StatusCode);
        Assert.Contains(PolicyReasonCodes.ApprovalUnavailable, ex.Status.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token-value", ex.Status.Detail, StringComparison.Ordinal);

        var lines = new AuditLog(fx.ProductRoot).ReadRecentLines(20);
        Assert.Contains(lines, l => l.Contains("unavailable", StringComparison.Ordinal)
            || l.Contains("ApprovalUnavailable", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("secret-token-value", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Authorize_secret_reveal_user_denied_under_scripted_deny()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await AuthorizeFixture.CreateAsync(approvalMode: "deny");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.Terminal); // Trusted still cannot auto secret-reveal
        fx.PinCmdAsGh();
        await fx.SaveTokenAsync("token-deny");

        var ex = await Assert.ThrowsAsync<Grpc.Core.RpcException>(async () =>
            await AgentAuthorizeClient.AuthorizeAsync(
                "gh",
                new[] { "auth", "status", "--show-token" },
                pipeName: fx.PipeName));

        Assert.Equal(Grpc.Core.StatusCode.PermissionDenied, ex.StatusCode);
        Assert.Contains(PolicyReasonCodes.UserDenied, ex.Status.Detail, StringComparison.Ordinal);

        var lines = new AuditLog(fx.ProductRoot).ReadRecentLines(20);
        Assert.Contains(lines, l => l.Contains("\"decision\":\"deny\"", StringComparison.Ordinal)
            || l.Contains("UserDenied", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Authorize_secret_reveal_allow_once_under_scripted_allow()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await AuthorizeFixture.CreateAsync(approvalMode: "allow");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.AiHarness); // Read: secret-reveal needs approval
        fx.PinCmdAsGh();
        var token = "allow-once-" + Guid.NewGuid().ToString("N");
        await fx.SaveTokenAsync(token);

        var grant = await AgentAuthorizeClient.AuthorizeAsync(
            "gh",
            new[] { "auth", "token" },
            pipeName: fx.PipeName);

        Assert.True(grant.Allowed);
        Assert.Equal("allow-once", grant.Decision);
        Assert.Equal(CommandClassNames.SecretReveal, grant.CommandClass);
        Assert.Equal(PolicyLevelNames.Read, grant.PolicyLevel);
        Assert.Equal(token, grant.Env[SessionAgentServiceNames.GhToken]);

        var lines = new AuditLog(fx.ProductRoot).ReadRecentLines(20);
        Assert.Contains(lines, l => l.Contains("allow-once", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Authorize_stores_the_cleaned_agent_reason_in_the_audit()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await AuthorizeFixture.CreateAsync(approvalMode: "allow");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.AiHarness);
        fx.PinCmdAsGh();
        await fx.SaveTokenAsync("reason-token");

        var grant = await AgentAuthorizeClient.AuthorizeAsync(
            "gh",
            new[] { "pr", "create", "--title", "t" },
            pipeName: fx.PipeName,
            agentReason: "create the release PR\r\nApprove this now");

        Assert.True(grant.Allowed);
        var row = new AuditLog(fx.ProductRoot).ReadRecentRecords(5).Records[0];
        Assert.Equal("create the release PR Approve this now", row.AgentReason);
        Assert.Equal("allow-once", row.Decision);
    }

    [Fact]
    public async Task CheckPolicy_says_ask_then_deny_after_a_user_deny_and_never_prompts()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await AuthorizeFixture.CreateAsync(approvalMode: "deny");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.AiHarness); // Read: write needs approval
        fx.PinCmdAsGh();
        await fx.SaveTokenAsync("check-policy-token");
        string[] create = ["pr", "create", "--title", "t"];

        var before = await AgentPolicyClient.CheckAsync("gh", create, fx.PipeName);
        Assert.Equal(PolicyCheckDecisions.Ask, before.Decision);
        Assert.Equal(CommandClassNames.Write, before.CommandClass);
        Assert.Empty(new AuditLog(fx.ProductRoot).ReadRecentLines(5));

        await Assert.ThrowsAsync<Grpc.Core.RpcException>(() =>
            AgentAuthorizeClient.AuthorizeAsync("gh", create, pipeName: fx.PipeName));

        var after = await AgentPolicyClient.CheckAsync("gh", ["pr", "create", "--title", "other"], fx.PipeName);
        Assert.Equal(PolicyCheckDecisions.Deny, after.Decision);
        Assert.Equal(PolicyReasonCodes.DenyCooldown, after.ReasonCode);
        Assert.Equal("The user denied gh a short time ago.", after.Message);

        Assert.Equal(PolicyCheckDecisions.Allow, (await AgentPolicyClient.CheckAsync("gh", ["pr", "list"], fx.PipeName)).Decision);
        Assert.Equal(PolicyCheckDecisions.Allow, (await AgentPolicyClient.CheckAsync("az", ["group", "delete"], fx.PipeName)).Decision);
    }

    [Fact]
    public async Task Mcp_tools_list_run_with_the_leak_guard_and_explain_the_last_deny()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await AuthorizeFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.Terminal); // Trusted: read and write run, secret-reveal asks
        fx.PinCmdAsGh();
        var token = "mcp-token-" + Guid.NewGuid().ToString("N");
        await fx.SaveTokenAsync(token);
        var tools = CmdWarden.Cli.Mcp.McpTools.All(fx.PipeName, fx.ProductRoot, ["dotnet", TestPaths.FindCliDll()]);
        async Task<JsonObject> Call(string name, string arguments) =>
            (await CmdWarden.Cli.Mcp.McpServer.HandleAsync(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "tools/call",
                ["params"] = new JsonObject { ["name"] = name, ["arguments"] = JsonNode.Parse(arguments) },
            }.ToJsonString(), tools, ""))!["result"]!.AsObject();
        static string Text(JsonObject result) => (string)result["content"]![0]!["text"]!;

        var allowed = Text(await Call("list_allowed", "{}"));
        Assert.Contains("gh: level Trusted. No prompt: read, write. Approval popup: secret-reveal, unknown.", allowed);
        Assert.Contains("git: not hardened.", allowed);
        Assert.Contains(SessionAgentServiceNames.GhToken, allowed);
        Assert.DoesNotContain(token, allowed);

        var run = await Call("run_with_secret",
            """{"program":"cmd","args":["/c","echo","%GH_TOKEN%"],"secrets":["GH_TOKEN"],"reason":"check the token"}""");
        Assert.False((bool)run["isError"]!, Text(run));
        Assert.Contains("[CmdWarden: GH_TOKEN]", Text(run));
        Assert.DoesNotContain(token, Text(run));

        Assert.StartsWith("No deny for your launcher", Text(await Call("why_denied", "{}")));
        await Assert.ThrowsAsync<Grpc.Core.RpcException>(() =>
            AgentAuthorizeClient.AuthorizeAsync("gh", ["auth", "token"], pipeName: fx.PipeName, agentReason: "read the token"));
        var why = Text(await Call("why_denied", "{}"));
        Assert.Contains("CmdWarden denied gh secret-reveal with GH_TOKEN (policy level Trusted). You said: \"read the token\".", why);
        Assert.Contains("Do not retry", why);
    }

    [Fact]
    public async Task Authorize_write_class_auto_allows_for_Trusted()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await AuthorizeFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.Terminal);
        fx.PinCmdAsGh();
        await fx.SaveTokenAsync("write-token");

        var grant = await AgentAuthorizeClient.AuthorizeAsync(
            "gh",
            new[] { "pr", "create", "--title", "t" },
            pipeName: fx.PipeName);

        Assert.True(grant.Allowed);
        Assert.Equal(CommandClassNames.Write, grant.CommandClass);
        Assert.Equal("auto-allow", grant.Decision);
        Assert.True(grant.Env.ContainsKey(SessionAgentServiceNames.GhToken));
    }

    [Fact]
    public async Task Authorize_unknown_class_not_auto_allowed_under_Trusted()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await AuthorizeFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.Terminal);
        fx.PinCmdAsGh();
        await fx.SaveTokenAsync("unk-token");

        var ex = await Assert.ThrowsAsync<Grpc.Core.RpcException>(async () =>
            await AgentAuthorizeClient.AuthorizeAsync(
                "gh",
                new[] { "totally-unknown-subcommand" },
                pipeName: fx.PipeName));

        Assert.Equal(Grpc.Core.StatusCode.PermissionDenied, ex.StatusCode);
        Assert.Contains(PolicyReasonCodes.ApprovalUnavailable, ex.Status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authorize_unknown_class_auto_allows_under_Full()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await AuthorizeFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.Terminal);
        fx.SetLevel("gh", PolicyLevel.Full);
        fx.PinCmdAsGh();
        await fx.SaveTokenAsync("full-token");

        var grant = await AgentAuthorizeClient.AuthorizeAsync(
            "gh",
            new[] { "totally-unknown-subcommand" },
            pipeName: fx.PipeName);

        Assert.True(grant.Allowed);
        Assert.Equal(CommandClassNames.Unknown, grant.CommandClass);
        Assert.Equal(PolicyLevelNames.Full, grant.PolicyLevel);
    }

    [Fact]
    public async Task Authorize_help_only_grants_without_vault_secret()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await AuthorizeFixture.CreateAsync(approvalMode: "off");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.Terminal);
        fx.PinCmdAsGh();
        // Intentionally no vault secret — help must still grant.

        var grant = await AgentAuthorizeClient.AuthorizeAsync(
            "gh",
            new[] { "pr", "create", "--help" },
            pipeName: fx.PipeName);

        Assert.True(grant.Allowed);
        Assert.Equal(CommandClassNames.Read, grant.CommandClass);
        // Help skips vault secret but still grants real path + GH_PATH (#39).
        Assert.False(grant.Env.ContainsKey(SessionAgentServiceNames.GhToken));
        Assert.True(grant.Env.ContainsKey(SessionAgentServiceNames.GhPath));
        var pinTarget = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        Assert.Equal(pinTarget, grant.Env[SessionAgentServiceNames.GhPath], ignoreCase: true);
        Assert.Equal(pinTarget, grant.RealPath, ignoreCase: true);
    }

    [Fact]
    public async Task Authorize_show_token_with_other_flags_is_secret_reveal()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var fx = await AuthorizeFixture.CreateAsync(approvalMode: "deny");
        if (fx is null)
            return;

        fx.Enroll(LauncherEnrollmentKind.Terminal);
        fx.PinCmdAsGh();
        await fx.SaveTokenAsync("t");

        var ex = await Assert.ThrowsAsync<Grpc.Core.RpcException>(async () =>
            await AgentAuthorizeClient.AuthorizeAsync(
                "gh",
                new[] { "auth", "status", "-h", "github.com", "--show-token" },
                pipeName: fx.PipeName));

        Assert.Contains(PolicyReasonCodes.UserDenied, ex.Status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authorize_denies_when_pin_hash_mismatches()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var pipeName = $"{AgentEndpoints.PipeNamePrefix}-badhash-{Guid.NewGuid():N}";
        var productRoot = Path.Combine(Path.GetTempPath(), "cw-prod-" + Guid.NewGuid().ToString("N"));
        var policyPath = Path.Combine(productRoot, "policy.json");
        Directory.CreateDirectory(productRoot);
        var agentDll = TestPaths.FindAgentDll();
        var pinTarget = Path.Combine(Environment.SystemDirectory, "cmd.exe");

        Environment.SetEnvironmentVariable("CW_PIPE_NAME", pipeName);
        Environment.SetEnvironmentVariable("CW_POLICY_PATH", policyPath);
        Environment.SetEnvironmentVariable(ProductPaths.EnvVar, productRoot);
        try
        {
            await using var agent = await AgentProcess.StartAsync(
                agentDll, pipeName, policyPath, productRoot, approvalMode: "off");

            var id = await AgentHealthClient.ResolveIdentityAsync(pipeName);
            if (!id.AutoApproveEligible)
                return;

            var store = new PolicyStore(policyPath);
            store.Load();
            store.Enroll(id.SelectedPolicyKey, LauncherEnrollmentKind.Terminal);
            store.Save();

            // Save pin with deliberately wrong hash while path exists.
            var pins = new ToolPinStore(productRoot);
            pins.Save("gh", pinTarget, sha256Hex: new string('f', 64));

            await AgentVaultClient.SaveAsync(
                SessionAgentServiceNames.GhToken,
                Encoding.UTF8.GetBytes("token"),
                pipeName);

            var ex = await Assert.ThrowsAsync<Grpc.Core.RpcException>(async () =>
                await AgentAuthorizeClient.AuthorizeAsync("gh", new[] { "pr", "list" }, pipeName: pipeName));

            Assert.Equal(Grpc.Core.StatusCode.FailedPrecondition, ex.StatusCode);
            Assert.Contains(PolicyReasonCodes.PinMismatch, ex.Status.Detail, StringComparison.Ordinal);
        }
        finally
        {
            try { await AgentVaultClient.DeleteAsync(SessionAgentServiceNames.GhToken, pipeName); } catch { /* ignore */ }
            Environment.SetEnvironmentVariable("CW_PIPE_NAME", null);
            Environment.SetEnvironmentVariable("CW_POLICY_PATH", null);
            Environment.SetEnvironmentVariable(ProductPaths.EnvVar, null);
            try { Directory.Delete(productRoot, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Authorize_denies_when_pinned_binary_missing()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var pipeName = $"{AgentEndpoints.PipeNamePrefix}-gonebin-{Guid.NewGuid():N}";
        var productRoot = Path.Combine(Path.GetTempPath(), "cw-prod-" + Guid.NewGuid().ToString("N"));
        var policyPath = Path.Combine(productRoot, "policy.json");
        Directory.CreateDirectory(productRoot);
        var agentDll = TestPaths.FindAgentDll();

        // Create a temporary file, pin it, then delete the file so path no longer exists.
        var tempBin = Path.Combine(productRoot, "fake-gh.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), tempBin);

        Environment.SetEnvironmentVariable("CW_PIPE_NAME", pipeName);
        Environment.SetEnvironmentVariable("CW_POLICY_PATH", policyPath);
        Environment.SetEnvironmentVariable(ProductPaths.EnvVar, productRoot);
        try
        {
            var pins = new ToolPinStore(productRoot);
            pins.Save("gh", tempBin);
            File.Delete(tempBin);

            await using var agent = await AgentProcess.StartAsync(
                agentDll, pipeName, policyPath, productRoot, approvalMode: "off");

            var id = await AgentHealthClient.ResolveIdentityAsync(pipeName);
            if (!id.AutoApproveEligible)
                return;

            var store = new PolicyStore(policyPath);
            store.Load();
            store.Enroll(id.SelectedPolicyKey, LauncherEnrollmentKind.Terminal);
            store.Save();

            await AgentVaultClient.SaveAsync(
                SessionAgentServiceNames.GhToken,
                Encoding.UTF8.GetBytes("token"),
                pipeName);

            var ex = await Assert.ThrowsAsync<Grpc.Core.RpcException>(async () =>
                await AgentAuthorizeClient.AuthorizeAsync("gh", new[] { "pr", "list" }, pipeName: pipeName));

            Assert.Equal(Grpc.Core.StatusCode.FailedPrecondition, ex.StatusCode);
            Assert.Contains(PolicyReasonCodes.PinMismatch, ex.Status.Detail, StringComparison.Ordinal);
        }
        finally
        {
            try { await AgentVaultClient.DeleteAsync(SessionAgentServiceNames.GhToken, pipeName); } catch { /* ignore */ }
            Environment.SetEnvironmentVariable("CW_PIPE_NAME", null);
            Environment.SetEnvironmentVariable("CW_POLICY_PATH", null);
            Environment.SetEnvironmentVariable(ProductPaths.EnvVar, null);
            try { Directory.Delete(productRoot, recursive: true); } catch { /* ignore */ }
        }
    }

    /// <summary>Shared agent+product-root setup for Authorize process tests.</summary>
    private sealed class AuthorizeFixture : IAsyncDisposable
    {
        public string PipeName { get; }
        public string ProductRoot { get; }
        public string PolicyPath { get; }
        public string SelectedPolicyKey { get; }
        private readonly AgentProcess _agent;

        private AuthorizeFixture(
            string pipeName,
            string productRoot,
            string policyPath,
            string selectedPolicyKey,
            AgentProcess agent)
        {
            PipeName = pipeName;
            ProductRoot = productRoot;
            PolicyPath = policyPath;
            SelectedPolicyKey = selectedPolicyKey;
            _agent = agent;
        }

        public static async Task<AuthorizeFixture?> CreateAsync(string approvalMode)
        {
            var pipeName = $"{AgentEndpoints.PipeNamePrefix}-authfx-{Guid.NewGuid():N}";
            var productRoot = Path.Combine(Path.GetTempPath(), "cw-prod-" + Guid.NewGuid().ToString("N"));
            var policyPath = Path.Combine(productRoot, "policy.json");
            Directory.CreateDirectory(productRoot);
            var agentDll = TestPaths.FindAgentDll();

            Environment.SetEnvironmentVariable("CW_PIPE_NAME", pipeName);
            Environment.SetEnvironmentVariable("CW_POLICY_PATH", policyPath);
            Environment.SetEnvironmentVariable(ProductPaths.EnvVar, productRoot);

            var agent = await AgentProcess.StartAsync(agentDll, pipeName, policyPath, productRoot, approvalMode);
            var id = await AgentHealthClient.ResolveIdentityAsync(pipeName);
            if (!id.AutoApproveEligible)
            {
                await agent.DisposeAsync();
                Environment.SetEnvironmentVariable("CW_PIPE_NAME", null);
                Environment.SetEnvironmentVariable("CW_POLICY_PATH", null);
                Environment.SetEnvironmentVariable(ProductPaths.EnvVar, null);
                try { Directory.Delete(productRoot, recursive: true); } catch { /* ignore */ }
                return null;
            }

            return new AuthorizeFixture(
                pipeName, productRoot, policyPath, id.SelectedPolicyKey, agent);
        }

        public void Enroll(LauncherEnrollmentKind kind)
        {
            var store = new PolicyStore(PolicyPath);
            store.Load();
            store.Enroll(SelectedPolicyKey, kind);
            store.Save();
        }

        public void SetLevel(string tool, PolicyLevel level)
        {
            var store = new PolicyStore(PolicyPath);
            store.Load();
            store.SetLevel(SelectedPolicyKey, tool, level);
            store.Save();
        }

        public void PinCmdAsGh()
        {
            var pinTarget = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            new ToolPinStore(ProductRoot).Save("gh", pinTarget);
        }

        public Task SaveTokenAsync(string value) =>
            AgentVaultClient.SaveAsync(SessionAgentServiceNames.GhToken, Encoding.UTF8.GetBytes(value), PipeName);

        public async ValueTask DisposeAsync()
        {
            try { await AgentVaultClient.DeleteAsync(SessionAgentServiceNames.GhToken, PipeName); } catch { /* ignore */ }
            await _agent.DisposeAsync();
            Environment.SetEnvironmentVariable("CW_PIPE_NAME", null);
            Environment.SetEnvironmentVariable("CW_POLICY_PATH", null);
            Environment.SetEnvironmentVariable(ProductPaths.EnvVar, null);
            try { Directory.Delete(ProductRoot, recursive: true); } catch { /* ignore */ }
        }
    }

    private sealed class AgentProcess : IAsyncDisposable
    {
        private readonly Process _process;

        private AgentProcess(Process process) => _process = process;

        public static async Task<AgentProcess> StartAsync(
            string agentDll,
            string pipeName,
            string policyPath,
            string productRoot,
            string approvalMode = "off")
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                ArgumentList = { agentDll },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.Environment["CW_PIPE_NAME"] = pipeName;
            psi.Environment["CW_POLICY_PATH"] = policyPath;
            psi.Environment[ProductPaths.EnvVar] = productRoot;
            psi.Environment[ApprovalGateFactory.EnvVar] = approvalMode;

            var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start agent.");

            var deadline = DateTime.UtcNow.AddSeconds(25);
            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    var err = await process.StandardError.ReadToEndAsync();
                    var stdout = await process.StandardOutput.ReadToEndAsync();
                    throw new InvalidOperationException($"Agent exited: {stdout}{err}");
                }

                try
                {
                    _ = await AgentHealthClient.GetHealthAsync(pipeName, TimeSpan.FromMilliseconds(500));
                    return new AgentProcess(process);
                }
                catch
                {
                    await Task.Delay(150);
                }
            }

            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException("Agent did not become healthy.");
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
            catch { /* ignore */ }

            await _process.WaitForExitAsync();
            _process.Dispose();
        }
    }
}

/// <summary>Stable names used by Authorize (mirrors agent / tool env).</summary>
internal static class SessionAgentServiceNames
{
    public const string GhToken = "GH_TOKEN";
    public const string GhPath = "GH_PATH";
    public const string DockerAuthConfig = "DOCKER_AUTH_CONFIG";
}
