using System.Text.Json;
using System.Text.Json.Serialization;

namespace CmdWarden.Contracts;

/// <summary>
/// Process exit codes for the Approval Gate helper (tickets #79 / #80).
/// </summary>
public static class ApprovalHelperExitCodes
{
    public const int AllowOnce = 0;
    public const int Deny = 1;
    public const int Unavailable = 2;
    public const int AllowForSession = 3;

    /// <summary>Added to an approve or a deny when the popup ran the Windows Hello step (#24).</summary>
    public const int HelloVerifiedFlag = 0x10;
    public const int HelloUnavailableFlag = 0x20;
    public const int HelloCanceledFlag = 0x40;

    /// <summary>Bits 8-9 carry the <see cref="SessionLength"/> of an "Allow for session" (#46).</summary>
    public const int LengthShift = 8;
    private const int LengthMask = 0x300;

    /// <summary>
    /// Exit code to the human answer. A Hello flag is valid only on the answer it can come with:
    /// verified or unavailable on an approve, canceled on a deny. A length is valid only on
    /// Allow for session. Any other code fails closed.
    /// </summary>
    public static ApprovalAnswer ToAnswer(int exitCode)
    {
        var length = (SessionLength)((exitCode & LengthMask) >> LengthShift);
        if (length != SessionLength.UntilExit)
        {
            var answer = ToAnswer(exitCode & ~LengthMask);
            return answer.Outcome == ApprovalOutcome.AllowForSession && Enum.IsDefined(length)
                ? answer with { Length = length }
                : new ApprovalAnswer(ApprovalOutcome.Unavailable);
        }
        var outcome = (exitCode & ~0x70) switch
        {
            AllowOnce => ApprovalOutcome.AllowOnce,
            Deny => ApprovalOutcome.Deny,
            AllowForSession => ApprovalOutcome.AllowForSession,
            _ => ApprovalOutcome.Unavailable,
        };
        var approve = outcome is ApprovalOutcome.AllowOnce or ApprovalOutcome.AllowForSession;
        return (exitCode & 0x70) switch
        {
            0 => new ApprovalAnswer(outcome),
            HelloVerifiedFlag when approve => new ApprovalAnswer(outcome, HelloCheck.Verified),
            HelloUnavailableFlag when approve => new ApprovalAnswer(outcome, HelloCheck.NotAvailable),
            HelloCanceledFlag when outcome == ApprovalOutcome.Deny => new ApprovalAnswer(outcome, HelloCheck.Canceled),
            _ => new ApprovalAnswer(ApprovalOutcome.Unavailable),
        };
    }

    /// <summary>The exit code for an answer; the inverse of <see cref="ToAnswer"/>.</summary>
    public static int FromAnswer(int baseCode, HelloCheck hello) => baseCode | hello switch
    {
        HelloCheck.Verified => HelloVerifiedFlag,
        HelloCheck.NotAvailable => HelloUnavailableFlag,
        HelloCheck.Canceled => HelloCanceledFlag,
        _ => 0,
    };

    /// <summary>Allow for session with the chosen length.</summary>
    public static int ForSession(SessionLength length) => AllowForSession | ((int)length << LengthShift);
}

/// <summary>
/// Serialize/deserialize helper payload (secret names only — never values).
/// </summary>
public static class ApprovalHelperJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(ApprovalHelperPayload payload) =>
        JsonSerializer.Serialize(payload, Options);

    public static ApprovalHelperPayload? TryDeserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<ApprovalHelperPayload>(json, Options);
        }
        catch
        {
            return null;
        }
    }
}
