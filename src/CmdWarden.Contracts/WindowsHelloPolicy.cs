namespace CmdWarden.Contracts;

/// <summary>
/// When the Approval Gate asks for Windows Hello after Approve (#24). A click proves little for
/// the most dangerous classes; an agent cannot fake a fingerprint, a face, or a PIN.
/// </summary>
public static class WindowsHelloPolicy
{
    public const string Off = "off";
    public const string SecretReveal = "secret-reveal";
    public const string WriteAndUp = "write-and-up";
    public const string Default = SecretReveal;

    public static readonly IReadOnlyList<string> Modes = [Off, SecretReveal, WriteAndUp];

    public static bool TryParse(string? text, out string mode)
    {
        mode = Modes.FirstOrDefault(m => string.Equals(m, text?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "";
        return mode.Length > 0;
    }

    /// <summary>An unknown mode asks for Hello on secret-reveal, like the default.</summary>
    public static bool Requires(string? mode, CommandClass commandClass) =>
        (TryParse(mode, out var m) ? m : Default) switch
        {
            Off => false,
            WriteAndUp => commandClass is not CommandClass.Read,
            _ => commandClass == CommandClass.SecretReveal,
        };
}

/// <summary>What the Windows Hello step gave, when the popup asked for it.</summary>
public enum HelloCheck
{
    NotAsked = 0,
    Verified = 1,
    /// <summary>Hello is not set up, not present, or disabled. The approve stands; the audit notes it.</summary>
    NotAvailable = 2,
    /// <summary>The person cancelled, or the retries ran out. The answer is Deny.</summary>
    Canceled = 3,
}

/// <summary>The human answer from the Approval Gate, with the Hello step (#24).</summary>
public readonly record struct ApprovalAnswer(ApprovalOutcome Outcome, HelloCheck Hello = HelloCheck.NotAsked)
{
    /// <summary>How long an Allow for session lasts (#46). Other outcomes keep the default.</summary>
    public SessionLength Length { get; init; }

    public static implicit operator ApprovalAnswer(ApprovalOutcome outcome) => new(outcome);

    /// <summary>Audit reason for the Hello step, or null when the popup did not ask.</summary>
    public string? HelloReason => Hello switch
    {
        HelloCheck.Verified => PolicyReasonCodes.HelloVerified,
        HelloCheck.NotAvailable => PolicyReasonCodes.HelloUnavailable,
        HelloCheck.Canceled => PolicyReasonCodes.HelloCanceled,
        _ => null,
    };

    /// <summary>
    /// UserConsentVerificationResult to a Hello check. Busy, retries exhausted, and cancel deny.
    /// No device, not set up, and disabled by policy fall back to the plain popup.
    /// </summary>
    public static HelloCheck FromVerificationResult(int result) => result switch
    {
        0 => HelloCheck.Verified,
        1 or 2 or 3 => HelloCheck.NotAvailable,
        _ => HelloCheck.Canceled,
    };
}
