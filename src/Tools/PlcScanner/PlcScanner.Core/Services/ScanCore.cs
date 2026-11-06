using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace PlcScanner.Core.Services;

/// <summary>一个被发现的设备（ARP 探测成功）。</summary>
/// <param name="IpNet">IP 的网络字节序 uint 表示，可直接比较大小。</param>
public sealed record FoundItem(uint IpNet, string Ip, string Mac);

/// <summary>
/// ARP 扫描核心：Windows SendARP API 封装、网段文本解析、TCP 端口探测。
/// 字节序约定：所有以 NetOrder 结尾的 uint 均为网络字节序数值
/// （第一八位组位于最低字节），数值递增恰好等于 IP 地址递增。
/// </summary>
public static class ScanCore
{
    /// <summary>
    /// ARP 探测并发度。注意：无响应地址每个约 6.5 秒超时，且系统 IP Helper 对
    /// 邻居解析有全局节流——并发超过几十后提速不明显，此值取平衡值。
    /// 已扫过的地址会留下邻居缓存，重复扫描同一网段会快得多。
    /// </summary>
    public const int Concurrency = 64;

    [DllImport("iphlpapi.dll")]
    private static extern int SendARP(uint destIP, uint srcIP, byte[] macAddr, ref uint phyAddrLen);

    /// <summary>
    /// 对目标 IP 做 ARP 探测。源地址选择策略：
    /// 1) 依次用与目标同网段的每个本地 IP 做源地址探测——多网卡配置在同一网段时，
    ///    系统自动源(src=0)按接口 metric 选出口，会从错误的网卡发出而漏掉设备
    ///    （例如 metric 最差的直连网卡上的 PLC）；
    /// 2) 没有任何同网段源（跨网段扫描）时，用第一个本地 IP 强制注入，从所选网卡发出。
    /// 返回 MAC 字符串，无响应返回 null。lastError 返回最终失败的 SendARP 错误码（诊断用）。
    /// </summary>
    public static string? ProbeArp(uint destIpNet, IReadOnlyList<(uint IpNet, uint MaskNet)> localIps, out int lastError)
    {
        lastError = 0;
        bool onLinkTried = false;
        foreach (var (src, mask) in localIps)
        {
            if (src == destIpNet || (src & mask) != (destIpNet & mask)) continue;
            onLinkTried = true;
            var (mac, err) = RawSendArp(destIpNet, src);
            if (mac != null) return mac;
            lastError = err;
        }
        if (!onLinkTried && localIps.Count > 0)
        {
            var (mac, err) = RawSendArp(destIpNet, localIps[0].IpNet);
            if (mac != null) return mac;
            lastError = err;
        }
        return null;
    }

    /// <summary>返回 (MAC 字符串或 null, SendARP 错误码)。</summary>
    public static (string? Mac, int Error) RawSendArp(uint destIpNet, uint srcIpNet)
    {
        var mac = new byte[6];
        uint len = 6;
        int rc = SendARP(destIpNet, srcIpNet, mac, ref len);
        if (rc != 0 || len != 6)
            return (null, rc);
        // 全零是回环假象，01-00-5E 是 IPv4 组播前缀，都不是真实主机
        if ((mac[0] | mac[1] | mac[2] | mac[3] | mac[4] | mac[5]) == 0) return (null, -1);
        if (mac[0] == 0x01 && mac[1] == 0x00 && mac[2] == 0x5E) return (null, -2);
        return ($"{mac[0]:X2}-{mac[1]:X2}-{mac[2]:X2}-{mac[3]:X2}-{mac[4]:X2}-{mac[5]:X2}", 0);
    }

    // ---------- 系统邻居表（ARP 缓存）清理 ----------
    // SendARP 的失败探测会在系统邻居表留下 Unreachable/Incomplete 负缓存，
    // 之后的探测命中负缓存直接失败、不再发包，导致同一网段越扫越少设备。
    // 扫描前删除目标范围内的非静态表项即可根治（需要管理员权限，失败时静默跳过）。

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_IPNETROW   // 原生结构 24 字节：4 个 DWORD + BYTE[8]，没有 State 字段
    {
        public uint IfIndex;
        public uint PhysAddrLen;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] PhysAddr;
        public uint Addr;  // 网络序 IP
        public uint Type;  // 1=other 2=invalid(负缓存) 3=dynamic 4=static/permanent
    }

    [DllImport("iphlpapi.dll")]
    private static extern int GetIpNetTable(IntPtr table, ref uint size, bool order);

    [DllImport("iphlpapi.dll")]
    private static extern int DeleteIpNetEntry(ref MIB_IPNETROW row);

    /// <summary>删除所有接口上、落在给定范围内的非静态邻居表项，返回删除条数。</summary>
    public static int ClearNeighbors(IReadOnlyList<(uint First, uint Last)> ranges)
    {
        uint size = 0;
        GetIpNetTable(IntPtr.Zero, ref size, false);
        if (size == 0 || size > 10_000_000) return 0;
        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetIpNetTable(buf, ref size, false) != 0) return 0;
            int count = Marshal.ReadInt32(buf);
            int rowSize = Marshal.SizeOf<MIB_IPNETROW>();
            var victims = new List<MIB_IPNETROW>();
            IntPtr p = (IntPtr)(buf.ToInt64() + sizeof(int));
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MIB_IPNETROW>(p);
                if (row.Type != 4 && InRanges(row.Addr, ranges))
                    victims.Add(row);
                p = (IntPtr)(p.ToInt64() + rowSize);
            }
            int removed = 0;
            foreach (var victim in victims)
            {
                MIB_IPNETROW row = victim;
                if (DeleteIpNetEntry(ref row) == 0) removed++;
            }
            return removed;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static bool InRanges(uint ipNet, IReadOnlyList<(uint First, uint Last)> ranges)
    {
        foreach (var (first, last) in ranges)
            if (ipNet >= first && ipNet <= last) return true;
        return false;
    }

    /// <summary>
    /// 从本机某网卡的地址列表里为 destIp 挑选源地址：优先与目标同网段的本地 IP，
    /// 都不同网段时用第一个（SendARP 指定 srcIP 后仍会从该 IP 所属网卡发出请求）。
    /// </summary>
    public static uint PickSourceIp(uint destIpNet, IReadOnlyList<(uint IpNet, uint MaskNet)> localIps)
    {
        foreach (var (ip, mask) in localIps)
            if ((ip & mask) == (destIpNet & mask))
                return ip;
        return localIps.Count > 0 ? localIps[0].IpNet : 0;
    }

    /// <summary>把 "a.b.c.d" 解析为网络序 uint。</summary>
    public static bool TryParseIp(string text, out uint ipNet)
    {
        ipNet = 0;
        var parts = text.Trim().Split('.');
        if (parts.Length != 4) return false;
        var b = new byte[4];
        for (int i = 0; i < 4; i++)
            if (!byte.TryParse(parts[i], out b[i])) return false;
        ipNet = (uint)(b[0] | b[1] << 8 | b[2] << 16 | b[3] << 24);
        return true;
    }

    /// <summary>网络序数值按 IP 大端语义字节反转，用于地址计数。</summary>
    public static uint ReverseBytes(uint u) =>
        (u & 0xFF) << 24 | (u & 0xFF00) << 8 | (u >> 8) & 0xFF00 | u >> 24;

    /// <summary>一段范围包含的地址个数（网络序数值的差不能直接当个数用）。</summary>
    public static long AddressCount(uint firstNet, uint lastNet) =>
        (long)ReverseBytes(lastNet) - ReverseBytes(firstNet) + 1;

    public static string IpToString(uint ipNet) =>
        $"{ipNet & 0xFF}.{(ipNet >> 8) & 0xFF}.{(ipNet >> 16) & 0xFF}.{(ipNet >> 24) & 0xFF}";

    /// <summary>
    /// 解析一段网段描述。支持格式（分号/逗号/换行分隔多段）：
    ///   192.168.1            整个 C 段 .1-.254
    ///   192.168.1.130        单个地址
    ///   192.168.1.0/24       CIDR
    ///   192.168.1.0/255.255.255.0   点分掩码
    ///   192.168.1.10-60      范围（末段，或写完整 IP）
    /// </summary>
    public static bool TryParseRange(string text, out uint firstNet, out uint lastNet, out string error)
    {
        firstNet = lastNet = 0;
        text = text.Trim();
        if (text.Length == 0) { error = "空网段"; return false; }

        // 范围形式
        int dash = text.IndexOf('-');
        if (dash > 0)
        {
            string left = text[..dash].Trim(), right = text[(dash + 1)..].Trim();
            if (!TryParseIp(left, out firstNet)) { error = $"范围起点 \"{left}\" 不是合法 IP"; return false; }
            if (uint.TryParse(right, out uint tail) && tail <= 255)
            {
                // 网络地址 .0 和广播地址 .255 没有 ARP 意义（.255 还会被本机应答），收窄到 .1-.254
                if (tail == 255) tail = 254;
                if (((firstNet >> 24) & 0xFF) == 0) firstNet += 0x01000000;
                lastNet = (firstNet & 0x00FFFFFF) | (tail << 24); // 保留前三段，替换末段
            }
            else if (TryParseIp(right, out lastNet)) { /* 完整 IP 形式的终点 */ }
            else { error = $"范围终点 \"{right}\" 无法识别"; return false; }
            // 交换按 IP 大端序比较：网络序数值的高低字节顺序与 IP 四段顺序相反，
            // 用数值比较会在跨段时把大小关系弄反
            if (ReverseBytes(lastNet) < ReverseBytes(firstNet)) (firstNet, lastNet) = (lastNet, firstNet);
            error = ""; return true;
        }

        // 掩码形式
        int slash = text.IndexOf('/');
        if (slash > 0)
        {
            string addrPart = text[..slash].Trim(), maskPart = text[(slash + 1)..].Trim();
            if (!TryParseIp(addrPart, out uint ip)) { error = $"\"{addrPart}\" 不是合法 IP"; return false; }
            uint mask;
            if (maskPart.Contains('.'))
            {
                if (!TryParseIp(maskPart, out mask)) { error = $"掩码 \"{maskPart}\" 不合法"; return false; }
            }
            else if (int.TryParse(maskPart, out int bits) && bits is >= 0 and <= 32)
            {
                // CIDR 的前缀位落在网络序数值的低位
                mask = bits == 0 ? 0 : 0xFFFFFFFFu >> (32 - bits);
            }
            else { error = $"掩码 \"{maskPart}\" 不合法"; return false; }
            uint network = ip & mask;
            uint broadcast = network | ~mask;
            // host 范围的 ±1 必须按 IP 大端语义做：网络序数值里第一段在最低字节，
            // 直接数值 +1 会进位到第一段（192.100.1.0/24 会算出 193.100.1.0）
            uint firstIp = ReverseBytes(network);
            uint lastIp = ReverseBytes(broadcast);
            if (lastIp - firstIp < 2)
            {
                (firstNet, lastNet) = (network, broadcast);
            }
            else
            {
                firstNet = ReverseBytes(firstIp + 1);
                lastNet = ReverseBytes(lastIp - 1);
            }
            error = ""; return true;
        }

        // 前三段（整个 C 段）或完整 IP
        int dots = text.Count(c => c == '.');
        if (dots == 2 && byte.TryParse(text.Split('.')[0], out var a)
                      && byte.TryParse(text.Split('.')[1], out var b)
                      && byte.TryParse(text.Split('.')[2], out var c))
        {
            firstNet = (uint)(a | b << 8 | c << 16 | 1u << 24);
            lastNet = firstNet + 253u * 0x01000000; // a.b.c.1 到 a.b.c.254（末段在网络序数值的最高字节）
            error = ""; return true;
        }
        if (TryParseIp(text, out uint single)) { firstNet = lastNet = single; error = ""; return true; }

        error = $"\"{text}\" 无法识别，支持 192.168.1 / 192.168.1.0/24 / 192.168.1.10-60";
        return false;
    }

    /// <summary>并发探测多个 TCP 端口，返回开放端口列表。</summary>
    public static async Task<int[]> ProbePortsAsync(string ip, int[] ports, int timeoutMs, CancellationToken ct)
    {
        var tasks = ports.Select(async p =>
        {
            try
            {
                using var tcp = new TcpClient();
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linked.CancelAfter(timeoutMs);
                await tcp.ConnectAsync(IPAddress.Parse(ip), p, linked.Token);
                return tcp.Connected ? p : -1;
            }
            catch { return -1; }
        });
        int[] result = await Task.WhenAll(tasks);
        return result.Where(p => p > 0).Order().ToArray();
    }
}
