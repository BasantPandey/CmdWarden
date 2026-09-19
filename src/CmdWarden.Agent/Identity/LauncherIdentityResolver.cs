using System.Runtime.Versioning;
using CmdWarden.Contracts;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Win32.SafeHandles;

namespace CmdWarden.Agent.Identity;

/// <summary>
/// Resolves hybrid Launcher identity for the current named-pipe gRPC caller.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LauncherIdentityResolver
{
    private readonly ProcessChainWalker _walker = new();
    private readonly ToolPinStore _pins;

    public LauncherIdentityResolver(ToolPinStore pins)
    {
        _pins = pins;
    }

    /// <summary>
    /// Prefer OS pipe client PID; optional claimedPid only as cross-check.
    /// </summary>
    public LauncherResolution Resolve(HttpContext? httpContext, int? claimedPid = null)
    {
        var (clientPid, fromPipe, notes) = ResolveClientPid(httpContext, claimedPid);
        var chain = _walker.Walk(clientPid).ToList();

        foreach (var node in chain)
        {
            if (node.PidReuseSuspected)
            {
                node.Kind = LauncherKinds.Unknown;
                node.PolicyKey = LauncherKinds.PolicyKeyUnknown;
                continue;
            }

            ImageIdentity.Populate(node);
        }

        // Select policy launcher: skip our own agent; prefer first non-unknown after client;
        // if client is cw/shim, look at parent chain for harness/terminal.
        // ponytail: _pins.All() reads every pin file per RPC; cache on ToolPinStore if probes get slow.
        var selected = SelectLauncher(chain, IsProductOwned(_pins.All(), ProductPaths.ShimsDir()));
        var eligible = selected.Kind is LauncherKinds.Authenticode or LauncherKinds.PathHash
            && !selected.PidReuseSuspected
            && selected.PolicyKey != LauncherKinds.PolicyKeyUnknown;

        if (!eligible)
            notes = Append(notes, "auto-approve ineligible (unknown or unverifiable launcher)");

        return new LauncherResolution
        {
            ClientPid = clientPid,
            ClientPidFromPipe = fromPipe,
            Selected = selected,
            Chain = chain,
            AutoApproveEligible = eligible,
            Notes = notes,
        };
    }

    private static (int Pid, bool FromPipe, string Notes) ResolveClientPid(HttpContext? httpContext, int? claimedPid)
    {
        var notes = "";
        if (httpContext is not null)
        {
            var feature = httpContext.Features.Get<IConnectionNamedPipeFeature>();
            var pipe = feature?.NamedPipe;
            if (pipe is not null)
            {
                if (NativeMethods.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid) && pid != 0)
                {
                    if (claimedPid is int c && c != (int)pid)
                        notes = $"claimed pid {c} mismatch pipe pid {pid}; using pipe pid";
                    return ((int)pid, true, notes);
                }

                notes = "GetNamedPipeClientProcessId failed";
            }
            else
            {
                notes = "no IConnectionNamedPipeFeature on connection";
            }
        }
        else
        {
            notes = "no HttpContext";
        }

        if (claimedPid is int claimed && claimed > 0)
            return (claimed, false, Append(notes, "fell back to claimed pid (not pipe-bound)"));

        // Last resort: current process (should not happen for real gRPC calls)
        return (Environment.ProcessId, false, Append(notes, "fell back to agent pid"));
    }

    /// <summary>
    /// Skip rule (#202, #205): a file under shims/ (shim or helper), a pinned real tool, a docker
    /// plugin directly below a pinned tool, or a git signer-walk node under the pinned git install
    /// root is never the launcher. A plugin name alone does not count; a renamed harness must not
    /// hide behind it.
    /// The skip covers the run of such nodes that starts at the client. A pinned tool deeper
    /// in a terminal chain does not move the launcher.
    /// </summary>
    public static Func<ProcessNode, ProcessNode?, bool> IsProductOwned(IReadOnlyList<ToolPin> pins, string shimsDir)
    {
        shimsDir = shimsDir.TrimEnd('\\') + "\\";
        var pinnedPaths = new HashSet<string>(pins.Select(p => p.Path), StringComparer.OrdinalIgnoreCase);
        var gitPin = pins.FirstOrDefault(p =>
            string.Equals(p.Tool, GitVaultNames.Tool, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(p.Path), "git.exe", StringComparison.OrdinalIgnoreCase));
        var gitRoot = gitPin is null ? null : HelperChainRule.InstallRoot(gitPin.Path);
        var gitThumb = gitPin?.SignerThumbprint;
        bool IsPinned(ProcessNode? n) => n?.Path is { } p && pinnedPaths.Contains(p);
        return (node, parent) =>
            IsPinned(node)
            || (node.Path is { } path && path.StartsWith(shimsDir, StringComparison.OrdinalIgnoreCase))
            || (HelperTools.IsDockerPlugin(node.FileName) && IsPinned(parent))
            || (gitRoot is not null
                && !string.IsNullOrEmpty(gitThumb)
                && HelperChainRule.IsSignerWalkNode(node, gitRoot, gitThumb));
    }

    public static ProcessNode SelectLauncher(
        IReadOnlyList<ProcessNode> chain,
        Func<ProcessNode, ProcessNode?, bool> isProductOwned)
    {
        var skip = 0;
        while (skip < chain.Count && isProductOwned(chain[skip], skip + 1 < chain.Count ? chain[skip + 1] : null))
            skip++;
        if (skip == chain.Count)
        {
            return new ProcessNode
            {
                Pid = 0,
                ParentPid = 0,
                Kind = LauncherKinds.Unknown,
                PolicyKey = LauncherKinds.PolicyKeyUnknown,
            };
        }
        if (skip > 0)
            chain = chain.Skip(skip).ToList();

        // Prefer first ancestor that looks like a real app (not only the immediate client),
        // but if client has authenticode/pathhash use client; for CLI client, walk to parent.
        var client = chain[0];
        var clientName = (client.FileName ?? "").ToLowerInvariant();
        var isOurCli = clientName is "cw.exe" or "cmdwarden.exe" or "dotnet.exe";

        if (!isOurCli && client.Kind != LauncherKinds.Unknown)
            return client;

        for (var i = 1; i < chain.Count; i++)
        {
            var n = chain[i];
            if (n.PidReuseSuspected)
                continue;
            if (n.Kind == LauncherKinds.Unknown)
                continue;
            var name = (n.FileName ?? "").ToLowerInvariant();
            if (name is "dotnet.exe" or "cw.exe" or "cmdwarden.exe")
                continue;
            return n;
        }

        // Fallback to client even if unknown
        return client;
    }

    /// <summary>
    /// An enrolled AI harness above the selected launcher wins. A harness that spawns
    /// bash.exe or pwsh.exe must not get the shell's terminal level.
    /// </summary>
    public static LauncherResolution PreferHarnessAncestor(
        LauncherResolution resolution,
        Func<string, bool> isHarnessKey)
    {
        var chain = resolution.Chain;
        var start = 0;
        for (var i = 0; i < chain.Count; i++)
        {
            if (ReferenceEquals(chain[i], resolution.Selected))
            {
                start = i + 1;
                break;
            }
        }

        if (isHarnessKey(resolution.Selected.PolicyKey))
            return resolution;

        for (var i = start; i < chain.Count; i++)
        {
            var n = chain[i];
            if (n.PidReuseSuspected || n.Kind == LauncherKinds.Unknown || !isHarnessKey(n.PolicyKey))
                continue;

            return new LauncherResolution
            {
                ClientPid = resolution.ClientPid,
                ClientPidFromPipe = resolution.ClientPidFromPipe,
                Selected = n,
                Chain = chain,
                AutoApproveEligible = n.Kind is LauncherKinds.Authenticode or LauncherKinds.PathHash,
                Notes = Append(resolution.Notes, $"ai-harness ancestor pid {n.Pid} selected over nearer launcher"),
            };
        }

        return resolution;
    }

    private static string Append(string existing, string add) =>
        string.IsNullOrEmpty(existing) ? add : existing + "; " + add;
}
