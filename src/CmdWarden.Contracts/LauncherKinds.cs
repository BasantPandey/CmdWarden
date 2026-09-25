namespace CmdWarden.Contracts;

/// <summary>
/// How a Launcher identity was established (hybrid L3).
/// </summary>
public static class LauncherKinds
{
    public const string Authenticode = "authenticode";
    public const string PathHash = "pathhash";
    public const string Unknown = "unknown";
    /// <summary>#36: the caller runs under an agent account; the account SID is the key.</summary>
    public const string Account = "account";

    public static string PolicyKeyAuthenticode(string sha1Thumbprint) =>
        "auth:sha1:" + sha1Thumbprint.ToLowerInvariant();

    public static string PolicyKeyPathHash(string sha256Hex) =>
        "pathhash:sha256:" + sha256Hex.ToLowerInvariant();

    public static string PolicyKeyUnknown => "unknown";
}
