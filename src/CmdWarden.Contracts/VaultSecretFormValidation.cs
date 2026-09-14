using System.Security.Cryptography;
using System.Text;

namespace CmdWarden.Contracts;

/// <summary>
/// Client-side Add-secret form rules for CmdWarden Vault (names + values).
/// Matches agent / <see cref="VaultNames"/> enforcement; never returns the value in error text.
/// </summary>
public static class VaultSecretFormValidation
{
    public const int MaxValueBytes = 2560;

    public readonly record struct Result(
        bool Ok,
        string NormalizedName,
        byte[]? ValueBytes,
        string? NameError,
        string? ValueError);

    /// <summary>
    /// Validate name + value for save. On success, <see cref="Result.ValueBytes"/> is a new UTF-8 buffer
    /// the caller must zero and free; on failure it is null.
    /// </summary>
    public static Result Validate(string? nameRaw, string? valueRaw)
    {
        string? nameErr = null;
        string? valueErr = null;
        var name = "";
        byte[]? valueBytes = null;

        try
        {
            if (string.IsNullOrWhiteSpace(nameRaw))
                nameErr = "Name is required.";
            else
            {
                _ = VaultNames.TargetName(nameRaw);
                name = NormalizeName(nameRaw);
            }
        }
        catch (ArgumentException ex)
        {
            nameErr = ex.Message;
        }

        if (string.IsNullOrEmpty(valueRaw))
        {
            valueErr = "Value is required.";
        }
        else
        {
            valueBytes = Encoding.UTF8.GetBytes(valueRaw);
            if (valueBytes.Length > MaxValueBytes)
            {
                valueErr = $"Value exceeds Credential Manager limit ({MaxValueBytes} bytes).";
                CryptographicOperations.ZeroMemory(valueBytes);
                valueBytes = null;
            }
        }

        var ok = nameErr is null && valueErr is null && name.Length > 0 && valueBytes is not null;
        if (!ok && valueBytes is not null)
        {
            CryptographicOperations.ZeroMemory(valueBytes);
            valueBytes = null;
        }

        return new Result(ok, name, valueBytes, nameErr, valueErr);
    }

    /// <summary>True when <paramref name="normalizedName"/> is already in the vault inventory (replace confirm).</summary>
    public static bool RequiresReplaceConfirm(string normalizedName, IEnumerable<string> existingNames) =>
        existingNames
            .Select(NormalizeName)
            .Any(n => n.Length > 0 && string.Equals(n, normalizedName, StringComparison.Ordinal));

    public static string NormalizeName(string raw)
    {
        var name = raw.Trim();
        if (name.StartsWith('+'))
            name = name[1..];
        return name;
    }

    public static string SanitizeError(string message)
    {
        if (string.IsNullOrEmpty(message))
            return "";
        return message.Length > 180 ? message[..180] + "..." : message;
    }
}
