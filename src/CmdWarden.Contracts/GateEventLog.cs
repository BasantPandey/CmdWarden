using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Win32;

namespace CmdWarden.Contracts;

/// <summary>
/// #48: a copy of each gate decision in the Windows Application log, for Event Viewer and a SIEM.
/// The event source is machine-wide, so <c>cw audit eventlog on</c> registers it once as admin.
/// The Session Agent writes only when the source is registered. An event has no secret value.
/// </summary>
public static class GateEventLog
{
    public const string Source = ProductInfo.Name;
    public const string LogName = "Application";
    public const string SourceKey = @"SYSTEM\CurrentControlSet\Services\EventLog\" + LogName + @"\" + Source;

    /// <summary>Windows ships this file with .NET Framework 4. Its text for each event id is the message itself.</summary>
    public const string MessageFile = @"%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\EventLogMessages.dll";

    public const int OtherId = 1000;

    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(SourceKey);
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>One event id per decision type: 1xxx allow, 2xxx deny, 3xxx leak guard.</summary>
    public static (int Id, EventLogEntryType Type) EventFor(string decision) => decision switch
    {
        GateDecisions.AutoAllow => (1001, EventLogEntryType.Information),
        GateDecisions.AllowOnce => (1002, EventLogEntryType.Information),
        GateDecisions.SessionGrant => (1003, EventLogEntryType.Information),
        GateDecisions.SessionAllow => (1004, EventLogEntryType.Information),
        GateDecisions.Deny => (2001, EventLogEntryType.Warning),
        GateDecisions.Unavailable => (2002, EventLogEntryType.Warning),
        GateDecisions.Redact => (3001, EventLogEntryType.Warning),
        _ => (OtherId, EventLogEntryType.Information),
    };

    /// <summary>"Label: value" lines that a person and a SIEM can read. Empty fields are left out.</summary>
    public static string Message(AuditGateRecord r)
    {
        var launcher = string.Join(", ", new[] { r.LauncherKind, r.EnrollmentKind }.Where(s => s.Length > 0));
        var lines = new (string Label, string? Value)[]
        {
            ("Tool", r.Tool),
            ("Command class", r.CommandClass),
            ("Policy level", r.PolicyLevel),
            ("Secret", r.SecretName),
            ("Purpose", r.Purpose),
            ("Reason code", r.ReasonCode),
            ("Grant length", r.GrantLength),
            ("Launcher", launcher.Length > 0 ? $"{r.LauncherPolicyKey} ({launcher})" : r.LauncherPolicyKey),
            ("Launcher path", r.LauncherPath),
            ("Client pid", r.ClientPid?.ToString(CultureInfo.InvariantCulture)),
            ("Agent account", r.AgentAccount),
            ("Agent says", AgentReason.Clean(r.AgentReason)),
            ("Time (UTC)", r.Ts),
        };
        var sb = new StringBuilder($"{Source} gate decision: {r.Decision}");
        foreach (var (label, value) in lines)
        {
            if (!string.IsNullOrWhiteSpace(value))
                sb.Append("\r\n").Append(label).Append(": ").Append(value);
        }
        return sb.ToString();
    }

    public static void TryWrite(AuditGateRecord record)
    {
        if (!IsRegistered())
            return;
        try
        {
            var (id, type) = EventFor(record.Decision);
            EventLog.WriteEntry(Source, Message(record), type, id);
        }
        catch
        {
            // The NDJSON row is the audit record. The event is a copy and never blocks a gate.
        }
    }

    /// <summary>Needs admin.</summary>
    public static void Register()
    {
        using var key = Registry.LocalMachine.CreateSubKey(SourceKey);
        key.SetValue("EventMessageFile", MessageFile, RegistryValueKind.ExpandString);
        key.SetValue("TypesSupported", 7, RegistryValueKind.DWord);
    }

    /// <summary>Needs admin. Events already written stay in the log.</summary>
    public static void Unregister() =>
        Registry.LocalMachine.DeleteSubKeyTree(SourceKey, throwOnMissingSubKey: false);
}
