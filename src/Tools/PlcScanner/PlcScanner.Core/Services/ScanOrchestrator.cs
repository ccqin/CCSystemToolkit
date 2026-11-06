using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Numerics;
using PlcScanner.Core.Models;

namespace PlcScanner.Core.Services;

/// <summary>网卡条目（名称/描述/IPv4+掩码列表）。</summary>
public sealed record NicEntry(string Display, List<(uint IpNet, uint MaskNet)> Ipv4s)
{
    public override string ToString() => Display;
}

/// <summary>
/// 扫描编排（从 WinForms 版 MainForm 迁移，UI 无关）：
/// 网卡枚举（虚拟过滤 + 自动源池）、网段解析、ARP 并发扫描、TCP 端口探测、
/// 结果经回调实时上报（UI 线程安全由调用方保证——Avalonia 用 Dispatcher 合并批量）。
/// </summary>
public sealed class ScanOrchestrator
{
    private static readonly string[] VirtualKeywords =
        { "vmware", "virtual", "hyper-v", "wsl", "loopback", "tap-", "tun ", "bluetooth", "蓝牙", "wan miniport", "ras ", "mirror" };

    // ---------- 网卡枚举 ----------

    /// <summary>枚举网卡（物理/无线；includeVirtual=false 时过滤虚拟网卡）。首项为“自动”（全部 Up 网卡的源地址池）。</summary>
    public static List<NicEntry> LoadNics(bool includeVirtual)
    {
        var result = new List<NicEntry>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211))
                continue;
            bool virtualNic = VirtualKeywords.Any(k => nic.Description.ToLowerInvariant().Contains(k));
            if (virtualNic && !includeVirtual) continue;

            var ips = ExtractIpv4(nic);
            string dot = nic.OperationalStatus == OperationalStatus.Up ? "●" : "○";
            string ipText = ips.Count > 0 ? string.Join(", ", ips.Select(x => ScanCore.IpToString(x.IpNet))) : "无 IPv4";
            result.Add(new NicEntry($"{dot} {nic.Name}  [{ipText}]  {nic.Description}", ips));
        }

        // “自动”项：全部 Up 网卡的 IPv4 源地址池（排除链路本地 169.254）
        var autoIps = new List<(uint IpNet, uint MaskNet)>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)) continue;
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            foreach (var (ip, mask) in ExtractIpv4(nic))
            {
                // 169.254.x.x 网络序最高字节是 169*256^0? 不对——末段在最高字节，169 在最低字节
                if ((ip & 0xFF) == 169 && ((ip >> 8) & 0xFF) == 254) continue;
                if (autoIps.All(x => x.IpNet != ip)) autoIps.Add((ip, mask));
            }
        }
        if (autoIps.Count > 0)
            result.Insert(0, new NicEntry($"★ 自动（全部在线网卡，共 {autoIps.Count} 个源地址）", autoIps));

        return result;
    }

    private static List<(uint IpNet, uint MaskNet)> ExtractIpv4(NetworkInterface nic)
    {
        var ips = new List<(uint, uint)>();
        foreach (var ua in nic.GetIPProperties().UnicastAddresses)
        {
            if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
            uint ipNet = IpToNetOrder(ua.Address);
            uint maskNet = ua.IPv4Mask is null ? 0xFFFFFFFFu : IpToNetOrder(ua.IPv4Mask);
            ips.Add((ipNet, maskNet));
        }
        return ips;
    }

    /// <summary>本机全部 IPv4（结果标注“本机”用）。</summary>
    public static HashSet<uint> GetAllLocalIps()
    {
        var set = new HashSet<uint>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                    set.Add(IpToNetOrder(ua.Address));
        return set;
    }

    /// <summary>选中网卡的网段 → CIDR 文本（填入输入框用）。</summary>
    public static List<string> NicRangesToCidr(NicEntry entry)
    {
        var lines = new List<string>();
        foreach (var (ip, mask) in entry.Ipv4s)
        {
            uint network = ip & mask;
            int bits = BitOperations.PopCount(mask);
            lines.Add($"{ScanCore.IpToString(network)}/{bits}");
        }
        return lines;
    }

    public static string NicIpsText(NicEntry entry) => entry.Ipv4s.Count == 0
        ? "本机地址: （该网卡无 IPv4）"
        : "本机地址: " + string.Join(", ",
            entry.Ipv4s.Select(x => $"{ScanCore.IpToString(x.IpNet)}/{BitOperations.PopCount(x.MaskNet)}"));

    // ---------- 扫描 ----------

    /// <summary>解析网段文本（多段以换行/分号/逗号分隔），返回 (范围列表, 错误列表)。</summary>
    public static (List<(uint First, uint Last)> Ranges, List<string> Errors) ParseRanges(string text)
    {
        var ranges = new List<(uint, uint)>();
        var errors = new List<string>();
        foreach (var raw in text.Split(new[] { '\n', '\r', ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (ScanCore.TryParseRange(raw, out uint first, out uint last, out string err))
                ranges.Add((first, last));
            else
                errors.Add(err);
        }
        return ([.. ranges.Distinct().OrderBy(r => r.Item1)], errors);
    }

    /// <summary>解析端口文本（逗号/分号分隔）。</summary>
    public static int[] ParsePorts(string text) =>
        [.. text.Split(',', ';')
            .Select(s => s.Trim()).Where(s => s.Length > 0)
            .Select(s => int.TryParse(s, out int p) ? p : -1)
            .Where(p => p is > 0 and < 65536)
            .Distinct()];

    /// <summary>一次完整扫描。回调均在后台线程触发，UI 侧需自行调度。</summary>
    public static async Task ScanAsync(
        NicEntry nic,
        IReadOnlyList<(uint First, uint Last)> ranges,
        int[] ports,
        bool probePorts,
        CancellationToken token,
        Action<string> onStatus,
        Action<int, int> onProgress,          // scanned, total
        Action<DeviceItem> onFound,
        Action<uint, int[]> onPortsDone)
    {
        long total = ranges.Sum(r => ScanCore.AddressCount(r.Item1, r.Item2));
        int totalInt = (int)Math.Min(total, int.MaxValue);
        var localIps = GetAllLocalIps();
        var foundAll = new ConcurrentDictionary<uint, DeviceItem>();
        int scanned = 0;

        onStatus("ARP 扫描中...");
        try
        {
            await Task.Run(() =>
            {
                int cleared = ScanCore.ClearNeighbors(ranges);
                if (cleared > 0)
                    onStatus($"已清理 {cleared} 条失效 ARP 缓存，扫描中...");

                var options = new ParallelOptions { MaxDegreeOfParallelism = ScanCore.Concurrency, CancellationToken = token };
                Parallel.ForEach(EnumerateAll(ranges), options, ipNet =>
                {
                    string? mac = ScanCore.ProbeArp(ipNet, nic.Ipv4s, out _);
                    int n = Interlocked.Increment(ref scanned);
                    if (mac != null)
                    {
                        var item = new DeviceItem
                        {
                            IpNet = ipNet,
                            Ip = ScanCore.IpToString(ipNet),
                            Mac = mac,
                            IsLocal = localIps.Contains(ipNet),
                        };
                        item.Note = item.IsLocal ? "本机" : "";
                        foundAll[ipNet] = item;
                        onFound(item);
                    }
                    if (n % 8 == 0 || n == totalInt) onProgress(n, totalInt);
                });
            }, token);

            if (probePorts && ports.Length > 0 && foundAll.Count > 0 && !token.IsCancellationRequested)
            {
                onStatus($"端口探测中（{foundAll.Count} 台设备，端口 {string.Join(",", ports)}）...");
                await Task.WhenAll(foundAll.Values.Select(async f =>
                {
                    int[] open = await ScanCore.ProbePortsAsync(f.Ip, ports, 600, token);
                    onPortsDone(f.IpNet, open);
                }));
            }
            onStatus(token.IsCancellationRequested
                ? $"已取消。共扫描 {scanned}/{totalInt}，发现 {foundAll.Count} 台设备"
                : $"扫描完成。{totalInt} 个地址，发现 {foundAll.Count} 台设备。双击行复制 IP。");
        }
        catch (OperationCanceledException)
        {
            onStatus($"已取消。共扫描 {scanned}/{totalInt}，发现 {foundAll.Count} 台设备");
        }
        catch (Exception ex)
        {
            onStatus($"扫描出错：{ex.Message}");
        }
    }

    private static IEnumerable<uint> EnumerateAll(IEnumerable<(uint First, uint Last)> ranges)
    {
        foreach (var (first, last) in ranges)
            for (uint u = first; ; u += 0x01000000)   // IP 末段在网络序数值最高字节
            {
                yield return u;
                if (u == last) break;
            }
    }

    private static uint IpToNetOrder(System.Net.IPAddress ip)
    {
        byte[] b = ip.GetAddressBytes();
        return (uint)(b[0] | b[1] << 8 | b[2] << 16 | b[3] << 24);
    }
}
