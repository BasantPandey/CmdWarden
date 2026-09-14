using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace CmdWarden.Agent.Identity;

/// <summary>
/// Walks parent process chain with PID-reuse detection.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessChainWalker
{
    private const int MaxDepth = 16;

    public IReadOnlyList<ProcessNode> Walk(int startPid)
    {
        var parentMap = BuildParentMap();
        var chain = new List<ProcessNode>();
        var seen = new HashSet<int>();
        var pid = startPid;
        DateTime? childCreate = null;

        for (var depth = 0; depth < MaxDepth && pid > 0; depth++)
        {
            if (!seen.Add(pid))
                break;

            parentMap.TryGetValue(pid, out var parentPid);
            var node = Describe(pid, parentPid);
            if (childCreate is not null && node.CreateTimeUtc is not null
                && node.CreateTimeUtc > childCreate)
            {
                node = new ProcessNode
                {
                    Pid = pid,
                    ParentPid = parentPid,
                    Path = node.Path,
                    FileName = node.FileName,
                    CreateTimeUtc = node.CreateTimeUtc,
                    PidReuseSuspected = true,
                };
            }

            chain.Add(node);
            childCreate = node.CreateTimeUtc;
            if (parentPid <= 0 || parentPid == pid)
                break;
            pid = parentPid;
        }

        return chain;
    }

    private static Dictionary<int, int> BuildParentMap()
    {
        var map = new Dictionary<int, int>();
        var snap = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.Th32CsSnapProcess, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateToolhelp32Snapshot failed.");

        try
        {
            var entry = new NativeMethods.PROCESSENTRY32
            {
                dwSize = (uint)Marshal.SizeOf<NativeMethods.PROCESSENTRY32>(),
            };

            if (!NativeMethods.Process32First(snap, ref entry))
                return map;

            do
            {
                map[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
            } while (NativeMethods.Process32Next(snap, ref entry));
        }
        finally
        {
            NativeMethods.CloseHandle(snap);
        }

        return map;
    }

    private static ProcessNode Describe(int pid, int parentPid)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, (uint)pid);
        if (handle == IntPtr.Zero)
        {
            return new ProcessNode
            {
                Pid = pid,
                ParentPid = parentPid,
            };
        }

        try
        {
            string? path = null;
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            if (NativeMethods.QueryFullProcessImageName(handle, 0, sb, ref size))
                path = sb.ToString(0, size);

            DateTime? create = null;
            if (NativeMethods.GetProcessTimes(handle, out var creation, out _, out _, out _))
                create = DateTime.FromFileTimeUtc(creation);

            return new ProcessNode
            {
                Pid = pid,
                ParentPid = parentPid,
                Path = path,
                FileName = path is null ? null : Path.GetFileName(path),
                CreateTimeUtc = create,
            };
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }
}
