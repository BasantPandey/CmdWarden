using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http;

namespace CmdWarden.Agent.Identity;

/// <summary>
/// The Windows account of a pipe caller (#36). The token of the caller process comes first. The
/// pipe token (Identification level) is the fallback when the agent may not open that process. A caller
/// that hides both is an unknown account, never the owner.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PipeCaller
{
    private const string ItemKey = "cw.account";
    public static readonly SecurityIdentifier Owner = WindowsIdentity.GetCurrent().User!;
    public static readonly SecurityIdentifier Unknown = new(WellKnownSidType.AnonymousSid, null);

    /// <summary>The caller account, or null when the call did not come through a pipe (in-process tests).</summary>
    public static SecurityIdentifier? Account(HttpContext? http)
    {
        if (http is null)
            return null;
        if (http.Items.TryGetValue(ItemKey, out var cached))
            return (SecurityIdentifier?)cached;
        var pipe = http.Features.Get<IConnectionNamedPipeFeature>()?.NamedPipe;
        SecurityIdentifier? sid = null;
        if (pipe is not null)
        {
            sid = NativeMethods.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid) && pid != 0 ? ProcessUser(pid) : null;
            sid ??= PipeUser(pipe);
            sid ??= Unknown;
        }
        http.Items[ItemKey] = sid;
        return sid;
    }

    /// <summary>True when the process runs as the owner of this agent (#41: a proxy client).</summary>
    public static bool IsOwnerProcess(int pid) => ProcessUser((uint)pid) is { } sid && sid.Equals(Owner);

    /// <summary>True when a pipe caller runs under another account than the owner of this agent.</summary>
    public static bool IsForeign(HttpContext? http) => Account(http) is { } sid && !sid.Equals(Owner);

    private static SecurityIdentifier? PipeUser(System.IO.Pipes.NamedPipeServerStream pipe)
    {
        SecurityIdentifier? sid = null;
        try
        {
            pipe.RunAsClient(() =>
            {
                using var id = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
                sid = id.User;
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
        return sid is null || sid.IsWellKnown(WellKnownSidType.AnonymousSid) ? null : sid;
    }

    private static SecurityIdentifier? ProcessUser(uint pid)
    {
        var process = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, pid);
        if (process == IntPtr.Zero)
            return null;
        try
        {
            if (!NativeMethods.OpenProcessToken(process, (uint)TokenAccessLevels.Query, out var token))
                return null;
            using (token)
            using (var id = new WindowsIdentity(token.DangerousGetHandle()))
                return id.User;
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }
}
