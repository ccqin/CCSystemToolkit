using System.Net;
using System.Runtime.InteropServices;

namespace NetRouteList.Core.Native;

// ==================== 数据结构（命名空间顶层，便于引用） ====================

// ---------------- SOCKADDR_INET（28 字节，对齐 4） ----------------
// union { SOCKADDR_IN Ipv4; SOCKADDR_IN6 Ipv6; }
//   offset 0  : sin_family            (2)
//   offset 2  : sin_port / sin6_port  (2)
//   offset 4  : sin_addr / sin6_flowinfo (4)
//   offset 8  : sin6_addr[0..15]      (16)
//   offset 24 : sin6_scope_id         (4)
[StructLayout(LayoutKind.Sequential)]
public struct SOCKADDR_INET
{
    public ushort Family;
    public ushort Port;
    public uint V4AddrOrFlowInfo;
    public uint V6Addr0;
    public uint V6Addr1;
    public uint V6Addr2;
    public uint V6Addr3;
    public uint V6ScopeId;

    public readonly bool IsIpv4 => Family == 2;
    public readonly bool IsIpv6 => Family == 23;

    public readonly IPAddress? ToIpAddress()
    {
        if (Family == 2)
            return new IPAddress(BitConverter.GetBytes(V4AddrOrFlowInfo));
        if (Family == 23)
        {
            var b = new byte[16];
            BitConverter.GetBytes(V6Addr0).CopyTo(b, 0);
            BitConverter.GetBytes(V6Addr1).CopyTo(b, 4);
            BitConverter.GetBytes(V6Addr2).CopyTo(b, 8);
            BitConverter.GetBytes(V6Addr3).CopyTo(b, 12);
            return new IPAddress(b, V6ScopeId);
        }
        return null;
    }

    /// <summary>是否为“空”下一跳（0.0.0.0 / ::），在路由表中表示“在链路上(on-link)”。</summary>
    public readonly bool IsOnLinkPlaceholder()
    {
        if (Family == 2) return V4AddrOrFlowInfo == 0;
        if (Family == 23) return V6Addr0 == 0 && V6Addr1 == 0 && V6Addr2 == 0 && V6Addr3 == 0;
        return false;
    }

    public static SOCKADDR_INET FromV4(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return new SOCKADDR_INET { Family = 2, V4AddrOrFlowInfo = BitConverter.ToUInt32(b, 0) };
    }

    public static SOCKADDR_INET FromV6(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return new SOCKADDR_INET
        {
            Family = 23,
            V6Addr0 = BitConverter.ToUInt32(b, 0),
            V6Addr1 = BitConverter.ToUInt32(b, 4),
            V6Addr2 = BitConverter.ToUInt32(b, 8),
            V6Addr3 = BitConverter.ToUInt32(b, 12),
            V6ScopeId = (uint)address.ScopeId,
        };
    }
}

// ---------------- IP_ADDRESS_PREFIX（32 字节，对齐 4） ----------------
[StructLayout(LayoutKind.Sequential)]
public struct IP_ADDRESS_PREFIX
{
    public SOCKADDR_INET Prefix;   // 28 字节
    public byte PrefixLength;      // 1 字节 + 3 填充
}

// ---------------- MIB_IPFORWARD_ROW2（104 字节，对齐 8） ----------------
// 注意：MSDN 语法页把 Origin 列在首位，但 Windows 实际 ABI（经内存转储逐字段验证）
// 将 NL_ROUTE_ORIGIN 排在结构末尾，InterfaceLuid 才是第一个字段。
[StructLayout(LayoutKind.Sequential)]
public struct MIB_IPFORWARD_ROW2
{
    public ulong InterfaceLuid;                 // 0
    public uint InterfaceIndex;                 // 8
    public IP_ADDRESS_PREFIX DestinationPrefix; // 12 (32 字节)
    public SOCKADDR_INET NextHop;               // 44 (28 字节)
    public byte SitePrefixLength;               // 72
    // （3 字节填充）
    public uint ValidLifetime;                  // 76
    public uint PreferredLifetime;              // 80
    public uint Metric;                         // 84  路由跃点（不含接口跃点）
    public int Protocol;                        // 88  NL_ROUTE_PROTOCOL
    public byte Loopback;                       // 92
    public byte AutoconfigureAddress;           // 93
    public byte Publish;                        // 94
    public byte Immortal;                       // 95
    public uint Age;                            // 96
    public int Origin;                          // 100 NL_ROUTE_ORIGIN
    // 总 104
}

[StructLayout(LayoutKind.Sequential)]
public struct MIB_IPFORWARD_TABLE2
{
    public uint NumEntries;
    public MIB_IPFORWARD_ROW2 Table;             // 位于偏移 8
}

// ---------------- MIB_IPINTERFACE_ROW（168 字节，对齐 8） ----------------
// 只消费其中的 Family / InterfaceLuid / InterfaceIndex / Metric / Connected，
// 但为偏移正确必须完整按 netioapi.h 声明。
[StructLayout(LayoutKind.Sequential)]
public struct MIB_IPINTERFACE_ROW
{
    public ushort Family;                                  // 0
    // （6 字节填充，使 ulong 按 8 对齐）
    public ulong InterfaceLuid;                            // 8
    public uint InterfaceIndex;                            // 16
    public uint MaxReassemblySize;                         // 20
    public ulong InterfaceIdentifier;                      // 24
    public uint MinRouterAdvertisementInterval;            // 32
    public uint MaxRouterAdvertisementInterval;            // 36
    public byte AdvertisingEnabled;                        // 40
    public byte ForwardingEnabled;                         // 41
    public byte WeakHostSend;                              // 42
    public byte WeakHostReceive;                           // 43
    public byte UseAutomaticMetric;                        // 44
    public byte UseNeighborUnreachabilityDetection;        // 45
    public byte ManagedAddressConfigurationSupported;      // 46
    public byte OtherStatefulConfigurationSupported;       // 47
    public byte AdvertiseDefaultRoute;                     // 48
    // （3 字节填充）
    public int RouterDiscoveryBehavior;                    // 52
    public uint DadTransmits;                              // 56
    public uint BaseReachableTime;                         // 60
    public uint RetransmitTime;                            // 64
    public uint PathMtuDiscoveryTimeout;                   // 68
    public int LinkLocalAddressBehavior;                   // 72
    public uint LinkLocalAddressTimeout;                   // 76
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public uint[] ZoneIndices;                             // 80..143
    public uint SitePrefixLength;                          // 144
    public uint Metric;                                    // 148  接口跃点
    public uint NlMtu;                                     // 152
    public byte Connected;                                 // 156
    public byte SupportsWakeUpPatterns;                    // 157
    public byte SupportsNeighborDiscovery;                 // 158
    public byte SupportsRouterDiscovery;                   // 159
    public uint ReachableTime;                             // 160
    public byte TransmitOffload;                           // 164
    public byte ReceiveOffload;                            // 165
    public byte DisableDefaultRoutes;                      // 166
    // 总 167 → 按 8 对齐取整为 168
}

// ==================== 函数导入 ====================

/// <summary>
/// Windows IP Helper API（iphlpapi.dll）的 P/Invoke 封装。仅支持 Windows。
/// 读取路由用 GetIpForwardTable2（不受系统显示语言影响），
/// 增删用 CreateIpForwardEntry2 / DeleteIpForwardEntry2（route.exe 同款底层接口）。
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class IpHelperNative
{
    public const ushort AF_INET = 2;
    public const ushort AF_INET6 = 23;

    /// <summary>无限生命周期 = 持久路由（写入注册表，重启后仍生效）。</summary>
    public const uint INFINITE_LIFETIME = 0xFFFFFFFF;

    public const int NO_ERROR = 0;
    public const int ERROR_ACCESS_DENIED = 5;
    public const int ERROR_INVALID_PARAMETER = 87;
    public const int ERROR_NOT_FOUND = 1168;
    public const int ERROR_OBJECT_ALREADY_EXISTS = 5010;

    [DllImport("iphlpapi.dll")]
    private static extern uint GetIpForwardTable2(ushort family, out IntPtr table);

    [DllImport("iphlpapi.dll")]
    private static extern void FreeMibTable(IntPtr memory);

    [DllImport("iphlpapi.dll")]
    private static extern void InitializeIpForwardEntry(ref MIB_IPFORWARD_ROW2 row);

    [DllImport("iphlpapi.dll")]
    private static extern uint CreateIpForwardEntry2(ref MIB_IPFORWARD_ROW2 row);

    [DllImport("iphlpapi.dll")]
    private static extern uint DeleteIpForwardEntry2(ref MIB_IPFORWARD_ROW2 row);

    [DllImport("iphlpapi.dll")]
    private static extern void InitializeIpInterfaceEntry(ref MIB_IPINTERFACE_ROW row);

    [DllImport("iphlpapi.dll")]
    private static extern uint GetIpInterfaceEntry(ref MIB_IPINTERFACE_ROW row);

    /// <summary>读取指定地址族（2=IPv4 / 23=IPv6）的整个路由表。</summary>
    public static List<MIB_IPFORWARD_ROW2> GetForwardTable(ushort family)
    {
        uint rc = GetIpForwardTable2(family, out IntPtr tablePtr);
        if (rc != NO_ERROR)
            throw new System.ComponentModel.Win32Exception((int)rc, $"GetIpForwardTable2 失败，错误码 {rc}");

        var rows = new List<MIB_IPFORWARD_ROW2>();
        try
        {
            int count = Marshal.ReadInt32(tablePtr);
            long firstOffset = Marshal.OffsetOf<MIB_IPFORWARD_TABLE2>(nameof(MIB_IPFORWARD_TABLE2.Table)).ToInt64();
            int rowSize = Marshal.SizeOf<MIB_IPFORWARD_ROW2>();
            for (int i = 0; i < count; i++)
            {
                IntPtr rowPtr = IntPtr.Add(tablePtr, (int)(firstOffset + (long)i * rowSize));
                rows.Add(Marshal.PtrToStructure<MIB_IPFORWARD_ROW2>(rowPtr));
            }
        }
        finally
        {
            FreeMibTable(tablePtr);
        }
        return rows;
    }

    /// <summary>新增一条路由。先经 <see cref="InitializeForwardRow"/> 初始化再填字段。</summary>
    public static uint CreateRoute(ref MIB_IPFORWARD_ROW2 row) => CreateIpForwardEntry2(ref row);

    /// <summary>删除一条路由。row 必须来自当前路由表的精确匹配行。</summary>
    public static uint DeleteRoute(ref MIB_IPFORWARD_ROW2 row) => DeleteIpForwardEntry2(ref row);

    public static void InitializeForwardRow(ref MIB_IPFORWARD_ROW2 row) => InitializeIpForwardEntry(ref row);

    /// <summary>查询接口在指定地址族下的 IP 接口信息（含接口跃点 Metric、是否已连接）。</summary>
    public static uint QueryIpInterface(ushort family, uint ifIndex, out MIB_IPINTERFACE_ROW row)
    {
        row = new MIB_IPINTERFACE_ROW { ZoneIndices = new uint[16] };
        InitializeIpInterfaceEntry(ref row);
        row.Family = family;
        row.InterfaceIndex = ifIndex;
        return GetIpInterfaceEntry(ref row);
    }

    /// <summary>调试用：把路由表缓冲区原始字节复制出来（用于核对原生布局）。</summary>
    public static byte[] DumpForwardTableBytes(ushort family, out int count)
    {
        uint rc = GetIpForwardTable2(family, out IntPtr tablePtr);
        if (rc != NO_ERROR)
            throw new System.ComponentModel.Win32Exception((int)rc, $"GetIpForwardTable2 失败，错误码 {rc}");
        try
        {
            count = Marshal.ReadInt32(tablePtr);
            int avail = count == 0 ? 16 : 16 + count * 260; // 给足余量，防止真实行比声明的大
            var buf = new byte[avail];
            Marshal.Copy(tablePtr, buf, 0, avail);
            return buf;
        }
        finally
        {
            FreeMibTable(tablePtr);
        }
    }
}
