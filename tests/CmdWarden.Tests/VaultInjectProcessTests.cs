using System.Diagnostics;
using System.Text;
using CmdWarden.Agent.Approval;
using CmdWarden.Cli;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// End-to-end save + inject through agent (issues #3, #5, #6).
/// </summary>
[Trait("Category", "Process")]
[Collection("AgentProcess")]
public class VaultInjectProcessTests
{
    [Fact]
    public async Task Save_and_inject_puts_secret_only_in_child_when_policy_allows()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var pipeName = $"{AgentEndpoints.PipeNamePrefix}-vault-{Guid.NewGuid():N}";
        var secretName = "cw_inj_" + Guid.NewGuid().ToString("N")[..10];
        var secretValue = "value-" + Guid.NewGuid().ToString("N");
        var agentDll = TestPaths.FindAgentDll();
        var policyDir = Path.Combine(Path.GetTempPath(), "cw-pol-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(policyDir);
        var policyPath = Path.Combine(policyDir, "policy.json");

        Environment.SetEnvironmentVariable("CW_PIPE_NAME", pipeName);
        Environment.SetEnvironmentVariable("CW_POLICY_PATH", policyPath);
        try
        {
            // Headless: auto-allow path must not depend on UI.
            await using var agent = await AgentProcess.StartAsync(agentDll, pipeName, policyPath, approvalMode: "off");

            var id = await AgentHealthClient.ResolveIdentityAsync(pipeName);
            Assert.True(
                id.AutoApproveEligible,
                "Test host launcher must be authenticode/pathhash eligible for inject test.");
            var store = new PolicyStore(policyPath);
            store.Load();
            store.Enroll(id.SelectedPolicyKey, LauncherEnrollmentKind.Terminal);
            store.Save();

            await AgentVaultClient.SaveAsync(secretName, Encoding.UTF8.GetBytes(secretValue), pipeName);

            Assert.NotEqual(secretValue, Environment.GetEnvironmentVariable(secretName));

            var released = await AgentVaultClient.ReleaseAsync(
                secretName,
                "test",
                pipeName,
                tool: "inject",
                commandClass: CommandClassNames.Write);
            var raw = released.Value.ToByteArray();
            var decoded = Encoding.UTF8.GetString(raw);
            Array.Clear(raw);
            Assert.Equal(secretValue, decoded);
            Assert.Equal(PolicyLevelNames.Trusted, released.PolicyLevel);
            Assert.Equal("auto-allow", released.Decision);

            var env = new Dictionary<string, string> { [secretName] = secretValue };
            var exit = InjectRunner.Run(
                "cmd.exe",
                new[] { "/c", $"if \"%{secretName}%\"==\"{secretValue}\" (exit 0) else (exit 7)" },
                env);
            Assert.Equal(0, exit);

            Assert.NotEqual(secretValue, Environment.GetEnvironmentVariable(secretName));
        }
        finally
        {
            try { new CredentialVault().Delete(secretName); } catch { /* ignore */ }

            Environment.SetEnvironmentVariable("CW_PIPE_NAME", null);
            Environment.SetEnvironmentVariable("CW_POLICY_PATH", null);
            try { Directory.Delete(policyDir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task ListSecretNames_reflects_save_and_delete_over_the_pipe()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var pipeName = $"{AgentEndpoints.PipeNamePrefix}-list-{Guid.NewGuid():N}";
        var name1 = "cw_ls_" + Guid.NewGuid().ToString("N")[..10];
        var name2 = "cw_ls_" + Guid.NewGuid().ToString("N")[..10];
        var agentDll = TestPaths.FindAgentDll();
        var policyDir = Path.Combine(Path.GetTempPath(), "cw-pol-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(policyDir);
        var policyPath = Path.Combine(policyDir, "policy.json");

        Environment.SetEnvironmentVariable("CW_PIPE_NAME", pipeName);
        Environment.SetEnvironmentVariable("CW_POLICY_PATH", policyPath);
        try
        {
            await using var agent = await AgentProcess.StartAsync(agentDll, pipeName, policyPath, approvalMode: "off");

            var namesBeforeSave = await AgentVaultClient.ListSecretNamesAsync(pipeName);
            Assert.DoesNotContain(name1, namesBeforeSave);
            Assert.DoesNotContain(name2, namesBeforeSave);

            await AgentVaultClient.SaveAsync(name1, Encoding.UTF8.GetBytes("v1"), pipeName);
            await AgentVaultClient.SaveAsync(name2, Encoding.UTF8.GetBytes("v2"), pipeName);

            var namesAfterSave = await AgentVaultClient.ListSecretNamesAsync(pipeName);
            Assert.Contains(name1, namesAfterSave);
            Assert.Contains(name2, namesAfterSave);

            await AgentVaultClient.DeleteAsync(name1, pipeName);

            var namesAfterDelete = await AgentVaultClient.ListSecretNamesAsync(pipeName);
            Assert.DoesNotContain(name1, namesAfterDelete);
            Assert.Contains(name2, namesAfterDelete);
        }
        finally
        {
            try { new CredentialVault().Delete(name1); } catch { /* ignore */ }
            try { new CredentialVault().Delete(name2); } catch { /* ignore */ }
            Environment.SetEnvironmentVariable("CW_PIPE_NAME", null);
            Environment.SetEnvironmentVariable("CW_POLICY_PATH", null);
            try { Directory.Delete(policyDir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Release_fails_closed_when_unenrolled_and_approval_unavailable()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var pipeName = $"{AgentEndpoints.PipeNamePrefix}-pol-{Guid.NewGuid():N}";
        var secretName = "cw_deny_" + Guid.NewGuid().ToString("N")[..10];
        var agentDll = TestPaths.FindAgentDll();
        var policyDir = Path.Combine(Path.GetTempPath(), "cw-pol-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(policyDir);
        var policyPath = Path.Combine(policyDir, "policy.json");
        File.WriteAllText(policyPath, """{"defaults":{"aiHarness":"Read","terminal":"Trusted"},"launchers":{}}""");

        Environment.SetEnvironmentVariable("CW_PIPE_NAME", pipeName);
        Environment.SetEnvironmentVariable("CW_POLICY_PATH", policyPath);
        try
        {
            await using var agent = await AgentProcess.StartAsync(agentDll, pipeName, policyPath, approvalMode: "off");
            await AgentVaultClient.SaveAsync(secretName, Encoding.UTF8.GetBytes("secret-value"), pipeName);

            var ex = await Assert.ThrowsAsync<Grpc.Core.RpcException>(async () =>
                await AgentVaultClient.ReleaseAsync(
                    secretName,
                    "test",
                    pipeName,
                    tool: "inject",
                    commandClass: CommandClassNames.Write));

            Assert.Equal(Grpc.Core.StatusCode.PermissionDenied, ex.StatusCode);
            Assert.Contains(PolicyReasonCodes.NotEnrolled, ex.Status.Detail, StringComparison.Ordinal);
        }
        finally
        {
            try { new CredentialVault().Delete(secretName); } catch { /* ignore */ }
            Environment.SetEnvironmentVariable("CW_PIPE_NAME", null);
            Environment.SetEnvironmentVariable("CW_POLICY_PATH", null);
            try { Directory.Delete(policyDir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Release_user_denied_when_approval_mode_is_deny()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var pipeName = $"{AgentEndpoints.PipeNamePrefix}-sr-{Guid.NewGuid():N}";
        var secretName = "cw_sr_" + Guid.NewGuid().ToString("N")[..10];
        var agentDll = TestPaths.FindAgentDll();
        var policyDir = Path.Combine(Path.GetTempPath(), "cw-pol-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(policyDir);
        var policyPath = Path.Combine(policyDir, "policy.json");

        Environment.SetEnvironmentVariable("CW_PIPE_NAME", pipeName);
        Environment.SetEnvironmentVariable("CW_POLICY_PATH", policyPath);
        try
        {
            await using var agent = await AgentProcess.StartAsync(agentDll, pipeName, policyPath, approvalMode: "deny");
            var id = await AgentHealthClient.ResolveIdentityAsync(pipeName);
            if (!id.AutoApproveEligible)
                return;

            var store = new PolicyStore(policyPath);
            store.Load();
            store.Enroll(id.SelectedPolicyKey, LauncherEnrollmentKind.Terminal);
            store.Save();

            await AgentVaultClient.SaveAsync(secretName, Encoding.UTF8.GetBytes("token"), pipeName);

            var ex = await Assert.ThrowsAsync<Grpc.Core.RpcException>(async () =>
                await AgentVaultClient.ReleaseAsync(
                    secretName,
                    "export",
                    pipeName,
                    tool: "gh",
                    commandClass: CommandClassNames.SecretReveal));

            Assert.Equal(Grpc.Core.StatusCode.PermissionDenied, ex.StatusCode);
            Assert.Contains(PolicyReasonCodes.UserDenied, ex.Status.Detail, StringComparison.Ordinal);
        }
        finally
        {
            try { new CredentialVault().Delete(secretName); } catch { /* ignore */ }
            Environment.SetEnvironmentVariable("CW_PIPE_NAME", null);
            Environment.SetEnvironmentVariable("CW_POLICY_PATH", null);
            try { Directory.Delete(policyDir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Release_allow_once_via_scripted_approval_for_secret_reveal()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var pipeName = $"{AgentEndpoints.PipeNamePrefix}-ao-{Guid.NewGuid():N}";
        var secretName = "cw_ao_" + Guid.NewGuid().ToString("N")[..10];
        var secretValue = "once-" + Guid.NewGuid().ToString("N");
        var agentDll = TestPaths.FindAgentDll();
        var policyDir = Path.Combine(Path.GetTempPath(), "cw-pol-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(policyDir);
        var policyPath = Path.Combine(policyDir, "policy.json");

        Environment.SetEnvironmentVariable("CW_PIPE_NAME", pipeName);
        Environment.SetEnvironmentVariable("CW_POLICY_PATH", policyPath);
        try
        {
            // Scripted Allow once (stands in for user clicking Yes on the native dialog).
            await using var agent = await AgentProcess.StartAsync(agentDll, pipeName, policyPath, approvalMode: "allow");
            var id = await AgentHealthClient.ResolveIdentityAsync(pipeName);
            if (!id.AutoApproveEligible)
                return;

            var store = new PolicyStore(policyPath);
            store.Load();
            store.Enroll(id.SelectedPolicyKey, LauncherEnrollmentKind.Terminal);
            store.Save();

            await AgentVaultClient.SaveAsync(secretName, Encoding.UTF8.GetBytes(secretValue), pipeName);

            var released = await AgentVaultClient.ReleaseAsync(
                secretName,
                "export",
                pipeName,
                tool: "gh",
                commandClass: CommandClassNames.SecretReveal);

            var raw = released.Value.ToByteArray();
            try
            {
                Assert.Equal(secretValue, Encoding.UTF8.GetString(raw));
                Assert.Equal("allow-once", released.Decision);
                Assert.Equal(CommandClassNames.SecretReveal, released.CommandClass);
            }
            finally
            {
                Array.Clear(raw);
            }
        }
        finally
        {
            try { new CredentialVault().Delete(secretName); } catch { /* ignore */ }
            Environment.SetEnvironmentVariable("CW_PIPE_NAME", null);
            Environment.SetEnvironmentVariable("CW_POLICY_PATH", null);
            try { Directory.Delete(policyDir, recursive: true); } catch { /* ignore */ }
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
            // Keep the audit rows of the test out of the real product root.
            psi.Environment[ProductPaths.EnvVar] = Path.GetDirectoryName(policyPath);
            psi.Environment[ApprovalGateFactory.EnvVar] = approvalMode;

            var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start agent.");

            var deadline = DateTime.UtcNow.AddSeconds(20);
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
