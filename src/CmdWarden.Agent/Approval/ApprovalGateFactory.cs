using System.Runtime.Versioning;
using CmdWarden.Contracts;

namespace CmdWarden.Agent.Approval;

/// <summary>
/// Selects Approval Gate implementation from CW_APPROVAL_MODE.
/// Default / prompt: process helper card (missing helper → Unavailable at Prompt).
/// messagebox/native: transitional MessageBox. allow|deny|session|off: CI scripted.
/// </summary>
public static class ApprovalGateFactory
{
    public const string EnvVar = "CW_APPROVAL_MODE";

    public static IApprovalGate CreateFromEnvironment()
    {
        var mode = Environment.GetEnvironmentVariable(EnvVar);
        return Create(mode);
    }

    [SupportedOSPlatform("windows")]
    public static IApprovalGate Create(string? mode)
    {
        // Ticket #82: interactive default is the modern process gate (helper under agent/approval-gate/).
        if (string.IsNullOrWhiteSpace(mode))
            return new ProcessApprovalGate();

        return mode.Trim().ToLowerInvariant() switch
        {
            "prompt" or "ui" or "winui" or "helper" or "process" or "default"
                => new ProcessApprovalGate(),
            "messagebox" or "native" or "msgbox"
                => new NativeApprovalGate(),
            "allow" or "auto-allow" or "yes"
                => new ScriptedApprovalGate(ApprovalOutcome.AllowOnce),
            "deny" or "auto-deny" or "no"
                => new ScriptedApprovalGate(ApprovalOutcome.Deny),
            "session" or "allow-for-session"
                => new ScriptedApprovalGate(ApprovalOutcome.AllowForSession),
            "off" or "none" or "headless" or "unavailable"
                => new UnavailableApprovalGate(),
            _ => new ProcessApprovalGate(),
        };
    }
}
