using System.Runtime.Versioning;
using System.Security.Principal;

namespace CmdWarden.Contracts;

/// <summary>
/// Windows accounts that run AI agents (#36), for example the Codex sandbox users. A call from such an
/// account uses the account as its launcher: the account is a stronger identity than the process chain.
/// The Session Agent pipe accepts an account only after <c>cw policy enroll --account</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AgentAccounts
{
    public const string PolicyKeyPrefix = "account:";

    /// <summary>
    /// Account names that CmdWarden knows as agent accounts. Codex makes these two on Windows.
    /// ponytail: Agent Workspace and MXC account names are not documented yet; enroll those by name.
    /// </summary>
    public static readonly string[] KnownNames = ["CodexSandboxOnline", "CodexSandboxOffline"];

    public static string PolicyKey(SecurityIdentifier sid) => PolicyKeyPrefix + sid.Value;

    /// <summary>The SID in an <c>account:</c> policy key, or null.</summary>
    public static SecurityIdentifier? SidOf(string policyKey)
    {
        if (!policyKey.StartsWith(PolicyKeyPrefix, StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            return new SecurityIdentifier(policyKey[PolicyKeyPrefix.Length..]);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The SID of an account name such as CodexSandboxOffline or PC\name, or null when it does not exist.</summary>
    public static SecurityIdentifier? TryFind(string accountName)
    {
        try
        {
            return (SecurityIdentifier)new NTAccount(accountName.Trim()).Translate(typeof(SecurityIdentifier));
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or ArgumentException or SystemException)
        {
            return null;
        }
    }

    /// <summary>"PC\name" for a SID, or the SID text when Windows cannot name it.</summary>
    public static string NameOf(SecurityIdentifier sid)
    {
        try
        {
            return sid.Translate(typeof(NTAccount)).Value;
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException)
        {
            return sid.Value;
        }
    }

    /// <summary>The known agent accounts that exist on this PC.</summary>
    public static IReadOnlyList<(string Name, SecurityIdentifier Sid)> OnThisPc() =>
        KnownNames.Select(n => (Name: n, Sid: TryFind(n))).Where(a => a.Sid is not null).Select(a => (a.Name, a.Sid!)).ToList();
}
