using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CmdWarden.Contracts;

namespace CmdWarden.Agent.Approval;

/// <summary>
/// Spike Approval Gate: modal MessageBox on an STA thread in the user session.
/// Yes = Allow once, No = Deny. Fail closed if non-interactive or the dialog fails.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NativeApprovalGate : IApprovalGate
{
    private static readonly TimeSpan DefaultTimeout = ApprovalGateTimeouts.Gate;

    private readonly TimeSpan _timeout;

    public NativeApprovalGate(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? DefaultTimeout;
    }

    public ApprovalAnswer Prompt(ApprovalRequest request)
    {
        if (!OperatingSystem.IsWindows())
            return ApprovalOutcome.Unavailable;

        // Session Agent is per-user interactive; still fail closed if no desktop session.
        if (!Environment.UserInteractive)
            return ApprovalOutcome.Unavailable;

        var body = ApprovalPromptText.BuildBody(request);
        var caption = ApprovalPromptText.Caption;
        ApprovalOutcome outcome = ApprovalOutcome.Unavailable;
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                // MB_YESNO | MB_ICONWARNING | MB_TOPMOST | MB_SETFOREGROUND
                const uint type = 0x00000004 | 0x00000030 | 0x00040000 | 0x00010000;
                var result = MessageBoxW(IntPtr.Zero, body, caption, type);
                outcome = result switch
                {
                    6 => ApprovalOutcome.AllowOnce, // IDYES
                    7 => ApprovalOutcome.Deny,      // IDNO
                    _ => ApprovalOutcome.Unavailable,
                };
            }
            catch (Exception ex)
            {
                error = ex;
                outcome = ApprovalOutcome.Unavailable;
            }
        });

        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!thread.Join(_timeout))
        {
            // Cannot abort MessageBox cleanly; leave thread background and fail closed.
            return ApprovalOutcome.Unavailable;
        }

        if (error is not null)
            return ApprovalOutcome.Unavailable;

        return outcome;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
