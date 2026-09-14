using CmdWarden.Contracts;

namespace CmdWarden.Agent.Identity;

/// <summary>
/// Who may call the credential helper gate (#202, #205). Docker: pinned tool at depth 1,
/// or one plugin above that. Git: signer walk from the helper to the pinned git.exe.
/// </summary>
public static class HelperChainRule
{
    /// <param name="chain">Caller chain, client first.</param>
    /// <param name="pin">Pin that already passed the hash check.</param>
    /// <returns>Null when the chain passes, else the reason code.</returns>
    public static string? Check(IReadOnlyList<ProcessNode> chain, ToolPin pin)
    {
        if (string.Equals(pin.Tool, GitVaultNames.Tool, StringComparison.OrdinalIgnoreCase))
            return CheckGit(chain, pin);

        if (chain.Count > 1 && IsPinnedTool(chain[1], pin))
            return null;
        if (chain.Count > 2 && HelperTools.IsDockerPlugin(chain[1].FileName) && IsPinnedTool(chain[2], pin))
            return null;
        return PolicyReasonCodes.HelperParentMissing;
    }

    public static bool IsPinnedTool(ProcessNode node, ToolPin pin) =>
        !node.PidReuseSuspected
        && node.Kind == LauncherKinds.Authenticode
        && string.Equals(node.Path, pin.Path, StringComparison.OrdinalIgnoreCase);

    private static readonly HashSet<string> GitLayoutDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd", "bin", "mingw64", "mingw32", "usr",
    };

    /// <summary>
    /// Git for Windows install root. Pins under <c>cmd\</c>, <c>bin\</c>, <c>mingw64\bin\</c>,
    /// or <c>usr\bin\</c> all map to the same root. Any other pin path uses its own directory.
    /// </summary>
    public static string InstallRoot(string pinPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(pinPath)) ?? "";
        while (GitLayoutDirs.Contains(Path.GetFileName(dir)) && Path.GetDirectoryName(dir) is { } parent)
            dir = parent;
        return dir;
    }

    /// <summary>
    /// A node the git signer walk may skip: Authenticode-valid, same signer as the pinned
    /// git.exe, living under the pinned install root.
    /// </summary>
    public static bool IsSignerWalkNode(ProcessNode node, string installRoot, string thumbprint)
    {
        if (node.PidReuseSuspected || node.Kind != LauncherKinds.Authenticode)
            return false;
        if (string.IsNullOrEmpty(thumbprint)
            || !string.Equals(node.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase))
            return false;
        return IsUnderRoot(node.Path, installRoot);
    }

    public static bool IsUnderRoot(string? path, string installRoot)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(installRoot))
            return false;
        var root = installRoot.TrimEnd('\\') + "\\";
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string? CheckGit(IReadOnlyList<ProcessNode> chain, ToolPin pin)
    {
        var pinIndex = -1;
        for (var i = 1; i < chain.Count; i++)
        {
            if (!IsPinnedTool(chain[i], pin))
                continue;
            pinIndex = i;
            break;
        }

        if (pinIndex < 0)
            return PolicyReasonCodes.HelperParentMissing;

        var thumb = pin.SignerThumbprint;
        if (string.IsNullOrEmpty(thumb))
            thumb = chain[pinIndex].Thumbprint;
        if (string.IsNullOrEmpty(thumb))
            return PolicyReasonCodes.HelperParentMissing;

        var root = InstallRoot(pin.Path);
        for (var i = 1; i < pinIndex; i++)
        {
            if (!IsSignerWalkNode(chain[i], root, thumb))
                return PolicyReasonCodes.HelperParentMissing;
        }

        return null;
    }
}
