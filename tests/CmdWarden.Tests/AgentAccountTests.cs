using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using CmdWarden.Agent;
using CmdWarden.Agent.Identity;
using CmdWarden.Cli;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>#36: an agent account is the launcher, and it reaches the agent only after enrollment.</summary>
public class AgentAccountTests
{
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);

    [Fact]
    public void Policy_key_holds_the_sid()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var key = AgentAccounts.PolicyKey(Users);
        Assert.Equal("account:S-1-5-32-545", key);
        Assert.Equal(Users, AgentAccounts.SidOf(key));
        Assert.Null(AgentAccounts.SidOf("auth:sha1:abc"));
        Assert.Null(AgentAccounts.SidOf("account:not-a-sid"));
        Assert.Null(AgentAccounts.TryFind("cw-no-such-user-" + Guid.NewGuid().ToString("N")[..8]));
        Assert.Equal(PipeCaller.Owner, AgentAccounts.TryFind(Environment.UserName));
    }

    private static LauncherResolution ChainResolution() => new()
    {
        ClientPid = 10,
        ClientPidFromPipe = true,
        Selected = new ProcessNode { Pid = 20, ParentPid = 1, Path = @"C:\tools\codex.exe", Kind = LauncherKinds.Authenticode, PolicyKey = "auth:sha1:abc" },
        Chain = [],
        AutoApproveEligible = true,
    };

    [Fact]
    public void A_foreign_account_replaces_the_chain_launcher_and_keeps_its_pid()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var chain = ChainResolution();
        Assert.Same(chain, LauncherIdentityResolver.ApplyAccount(chain, null));
        Assert.Same(chain, LauncherIdentityResolver.ApplyAccount(chain, PipeCaller.Owner));

        var account = LauncherIdentityResolver.ApplyAccount(chain, Users);
        Assert.Equal("account:S-1-5-32-545", account.Selected.PolicyKey);
        Assert.Equal(LauncherKinds.Account, account.Selected.Kind);
        Assert.Equal(20, account.Selected.Pid);
        Assert.Equal(@"BUILTIN\Users", account.AgentAccount);
        Assert.True(account.AutoApproveEligible);

        var hidden = LauncherIdentityResolver.ApplyAccount(chain, PipeCaller.Unknown);
        Assert.Equal(LauncherKinds.PolicyKeyUnknown, hidden.Selected.PolicyKey);
        Assert.False(hidden.AutoApproveEligible);
    }

    [Fact]
    public void An_agent_account_may_use_the_gate_calls_only()
    {
        Assert.Equal(
            ["Authorize", "CheckPolicy", "GetHealth", "HelperCredential", "ReleaseSecret", "ResolveCallerIdentity"],
            OwnerOnlyInterceptor.AgentAccountMethods.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Audit_and_shim_text_name_the_account()
    {
        const string line = """{"ts":"2026-09-26T10:00:00Z","decision":"auto-allow","tool":"git","launcherPolicyKey":"account:S-1-5-21-1-2-3-1004","agentAccount":"PC\\CodexSandboxOffline"}""";
        Assert.Contains(@"launcher=PC\CodexSandboxOffline (agent account)", AuditFormatter.FormatLine(line));
        Assert.Equal(@"PC\CodexSandboxOffline", AuditGateRecord.TryParse(line)!.AgentAccount);
        Assert.Contains("cw policy enroll --account", ShimStopText.AccountRefused());
    }

    [Trait("Category", "Process")]
    [Collection("AgentProcess")]
    public class PipeAccess
    {
        private static IEnumerable<PipeAccessRule> Rules(string pipeName)
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            client.Connect(5000);
            return client.GetAccessControl().GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToList();
        }

        [Fact]
        public async Task The_pipe_opens_to_an_enrolled_account_after_a_restart_and_to_nobody_else()
        {
            if (!OperatingSystem.IsWindows())
                return;
            await using var fx = await ApprovalMemoryFixture.CreateAsync("off");
            if (fx is null)
                return;
            Assert.DoesNotContain(Rules(fx.PipeName), r => r.IdentityReference.Equals(Users));

            var store = new PolicyStore(fx.PolicyPath);
            store.Load();
            store.Enroll(AgentAccounts.PolicyKey(Users), LauncherEnrollmentKind.AiHarness, @"BUILTIN\Users");
            store.Save();
            await fx.RestartAsync();

            var rules = Rules(fx.PipeName);
            var account = Assert.Single(rules, r => r.IdentityReference.Equals(Users));
            Assert.Equal(AccessControlType.Allow, account.AccessControlType);
            Assert.True(account.PipeAccessRights.HasFlag(PipeAccessRights.ReadWrite));
            Assert.False(account.PipeAccessRights.HasFlag(PipeAccessRights.ChangePermissions));
            Assert.Contains(rules, r => r.IdentityReference.Equals(PipeCaller.Owner));
            Assert.All(rules, r => Assert.True(r.IdentityReference.Equals(PipeCaller.Owner) || r.IdentityReference.Equals(Users)));
            Assert.True((await AgentHealthClient.GetHealthAsync(fx.PipeName)).Alive);
        }
    }
}
