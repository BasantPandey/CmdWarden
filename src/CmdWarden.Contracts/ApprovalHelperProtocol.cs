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

    public static ApprovalOutcome ToOutcome(int exitCode) => exitCode switch
    {
        AllowOnce => ApprovalOutcome.AllowOnce,
        Deny => ApprovalOutcome.Deny,
        AllowForSession => ApprovalOutcome.AllowForSession,
        _ => ApprovalOutcome.Unavailable,
    };
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
