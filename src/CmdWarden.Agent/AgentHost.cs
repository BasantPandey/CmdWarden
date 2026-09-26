using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using CmdWarden.Agent.Approval;
using CmdWarden.Agent.Identity;
using CmdWarden.Agent.Proxy;
using CmdWarden.Agent.Ssh;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Proxy;
using CmdWarden.Contracts.Ssh;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace CmdWarden.Agent;

/// <summary>
/// Builds and runs the per-user Session Agent (gRPC over named pipe).
/// </summary>
[SupportedOSPlatform("windows")]
public static class AgentHost
{
    public static WebApplication Build(string[]? args = null, string? pipeName = null, string? policyPath = null)
    {
        pipeName ??= AgentEndpoints.PipeName;
        policyPath ??= PolicyStore.DefaultPath();
        var productRoot = ProductPaths.Root();

        var builder = WebApplication.CreateBuilder(args ?? Array.Empty<string>());
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();

        var policy = new PolicyStore(policyPath);
        policy.Load();
        // Tight ACL: same user + elevation only (not default Everyone-open pipe SD).
        // #36: plus each agent account that the person enrolled. The account set is read at start.
        var accounts = policy.Launchers.Keys.Select(AgentAccounts.SidOf).OfType<SecurityIdentifier>().ToList();
        builder.WebHost.UseNamedPipes(options =>
        {
            if (accounts.Count == 0)
            {
                options.CurrentUserOnly = true;
                return;
            }
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.SetOwner(PipeCaller.Owner);
            security.AddAccessRule(new PipeAccessRule(PipeCaller.Owner, PipeAccessRights.FullControl, AccessControlType.Allow));
            foreach (var sid in accounts)
                security.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, AccessControlType.Allow));
            options.CurrentUserOnly = false;
            options.PipeSecurity = security;
        });

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenNamedPipe(pipeName, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CW_PIPE_NAME"] = pipeName,
            ["CW_POLICY_PATH"] = policyPath,
            ["CW_PRODUCT_ROOT"] = productRoot,
        });

        var approvalGate = ApprovalGateFactory.CreateFromEnvironment();
        var pins = new ToolPinStore(productRoot);
        var audit = new AuditLog(productRoot);
        // Housekeeping only; never blocks agent start or fail-closed grant audit writes (#38).
        audit.PruneOlderThan();

        var memory = new ApprovalMemory();
        // Workstation lock: memory never outlives the session it was granted in (#133).
        if (OperatingSystem.IsWindows())
            Microsoft.Win32.SystemEvents.SessionSwitch += (_, e) =>
            {
                if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock)
                    memory.ClearAll();
            };

        builder.Services.AddGrpc(o => o.Interceptors.Add<OwnerOnlyInterceptor>());
        builder.Services.AddSingleton(new AgentRuntimeInfo(pipeName, policyPath, productRoot));
        builder.Services.AddSingleton(policy);
        builder.Services.AddSingleton<IApprovalGate>(approvalGate);
        builder.Services.AddSingleton(memory);
        builder.Services.AddSingleton(pins);
        builder.Services.AddSingleton(audit);
        builder.Services.AddSingleton(new AlarmNotifier(approvalGate is ProcessApprovalGate));
        builder.Services.AddSingleton<CredentialVault>();
        builder.Services.AddSingleton<LauncherIdentityResolver>();
        // One service for every call, so the popup lock and the caches hold across calls; the ssh gate uses it too.
        builder.Services.AddSingleton<SessionAgentService>();
        // #41: the placeholder proxy runs when cw proxy setup wrote its config.
        if (File.Exists(KeyProxy.ConfigPath(productRoot)))
            builder.Services.AddHostedService<KeyProxyServer>();
        if (LoadSshGate(productRoot) is { } ssh)
        {
            builder.Services.AddSingleton(ssh);
            builder.Services.AddHostedService<SshAgentProxy>();
        }

        var app = builder.Build();
        app.MapGrpcService<SessionAgentService>();
        app.MapGet("/", () => $"{ProductInfo.Name} Session Agent - use gRPC on named pipe '{pipeName}'.");
        return app;
    }

    /// <summary>The ssh gate runs when cw harden ssh wrote ssh.json (#39). A bad file must not stop the agent.</summary>
    private static SshGateState? LoadSshGate(string productRoot)
    {
        try
        {
            return SshGate.Load(productRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"ssh gate off: cannot read {SshGate.StatePath(productRoot)}: {ex.Message}");
            return null;
        }
    }
}

public sealed record AgentRuntimeInfo(string PipeName, string PolicyPath, string ProductRoot);
