using System.Diagnostics;
using System.Runtime.Versioning;

namespace CmdWarden.Agent.Approval;

/// <summary>
/// Shows the canary alarm (#29) as a Windows notification through the Approval Gate helper
/// (<c>--alarm</c>). Only with the interactive gate: scripted and headless modes stay silent.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AlarmNotifier(bool interactive)
{
    public void Show(string text)
    {
        if (!interactive || ApprovalGateLocator.FindHelperPath() is not { } helper)
            return;
        try
        {
            var psi = new ProcessStartInfo(helper) { UseShellExecute = false };
            psi.ArgumentList.Add("--alarm");
            psi.ArgumentList.Add(text);
            Process.Start(psi)?.Dispose();
        }
        catch
        {
            // The block and the audit row already happened; the notification is best effort.
        }
    }
}
