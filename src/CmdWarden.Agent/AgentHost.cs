using System.Runtime.Versioning;
using CmdWarden.Agent.Approval;
using CmdWarden.Agent.Identity;
using CmdWarden.Contracts;
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

        // Tight ACL: same user + elevation only (not default Everyone-open pipe SD).
        builder.WebHost.UseNamedPipes(options =>
        {
            options.CurrentUserOnly = true;
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

        var policy = new PolicyStore(policyPath);
        policy.Load();
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

        builder.Services.AddGrpc();
        builder.Services.AddSingleton(new AgentRuntimeInfo(pipeName, policyPath, productRoot));
        builder.Services.AddSingleton(policy);
        builder.Services.AddSingleton<IApprovalGate>(approvalGate);
        builder.Services.AddSingleton(memory);
        builder.Services.AddSingleton(pins);
        builder.Services.AddSingleton(audit);
        builder.Services.AddSingleton(new AlarmNotifier(approvalGate is ProcessApprovalGate));
        builder.Services.AddSingleton<CredentialVault>();
        builder.Services.AddSingleton<LauncherIdentityResolver>();

        var app = builder.Build();
        app.MapGrpcService<SessionAgentService>();
        app.MapGet("/", () => $"{ProductInfo.Name} Session Agent - use gRPC on named pipe '{pipeName}'.");
        return app;
    }
}

public sealed record AgentRuntimeInfo(string PipeName, string PolicyPath, string ProductRoot);
