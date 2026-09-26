using System.Buffers.Binary;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CmdWarden.Agent.Proxy;

/// <summary>
/// The process that owns the client end of a loopback TCP connection (#41), from the IPv4 TCP
/// table of Windows. A proxy client gives no pid, so this is how the gate finds the launcher.
/// </summary>
[SupportedOSPlatform("windows")]
public static class TcpOwner
{
    /// <param name="client">The remote end of the accepted socket: the client.</param>
    /// <param name="server">The local end of the accepted socket: the proxy.</param>
    public static int? Find(IPEndPoint client, IPEndPoint server)
    {
        var size = 0;
        _ = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AfInet, TcpTableOwnerPidConnections, 0);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var result = GetExtendedTcpTable(buffer, ref size, false, AfInet, TcpTableOwnerPidConnections, 0);
                if (result == ErrorInsufficientBuffer)
                    continue;
                if (result != 0)
                    return null;
                var count = Marshal.ReadInt32(buffer);
                var rowSize = Marshal.SizeOf<Row>();
                for (var i = 0; i < count; i++)
                {
                    var row = Marshal.PtrToStructure<Row>(buffer + 4 + i * rowSize);
                    // The client row: its local end is the client, its remote end is the proxy.
                    if (Port(row.LocalPort) == client.Port && Port(row.RemotePort) == server.Port
                        && row.LocalAddr == Address(client.Address) && row.RemoteAddr == Address(server.Address))
                        return (int)row.OwningPid;
                }
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        return null;
    }

    /// <summary>The port is in network order in the low 16 bits.</summary>
    private static int Port(uint value) => BinaryPrimitives.ReverseEndianness((ushort)value);

    private static uint Address(IPAddress address) => BitConverter.ToUInt32(address.MapToIPv4().GetAddressBytes());

    private const int AfInet = 2;
    private const int TcpTableOwnerPidConnections = 4;
    private const uint ErrorInsufficientBuffer = 122;

    [StructLayout(LayoutKind.Sequential)]
    private struct Row
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int af, int tableClass, uint reserved);
}
