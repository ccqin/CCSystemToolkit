using System.Runtime.InteropServices;

namespace NetTraffic.Core.Services;

/// <summary>
/// TCP 连接数统计：GetExtendedTcpTable(TCP_TABLE_OWNER_PID_ALL) → PID → 连接数。
/// </summary>
public static class ConnectionCounter
{
    public static Dictionary<int, int> CountByPid()
    {
        var result = new Dictionary<int, int>();
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2 /*AF_INET*/, 5 /*TCP_TABLE_OWNER_PID_ALL*/, 0);
        if (size <= 0 || size > 4_000_000) return result;

        var buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buf, ref size, false, 2, 5, 0) != 0) return result;
            int count = Marshal.ReadInt32(buf);
            int rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
            IntPtr p = buf + sizeof(int);
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(p);
                if (row.owningPid > 0)
                {
                    result.TryGetValue(row.owningPid, out int n);
                    result[row.owningPid] = n + 1;
                }
                p += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
        public int owningPid;
    }

    [DllImport("iphlpapi.dll")]
    private static extern int GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen, bool bOrder,
        int ulAf, int tableClass, int reserved);
}
