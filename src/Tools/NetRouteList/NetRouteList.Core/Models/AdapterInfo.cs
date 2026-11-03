using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NetRouteList.Core.Models;

/// <summary>网卡上单个 IP 地址的信息。</summary>
public sealed class IpAddressInfo
{
    public required IPAddress Address { get; init; }
    public required int PrefixLength { get; init; }
    /// <summary>地址来源：DHCP / 手动 / 其他。</summary>
    public string Origin { get; init; } = "";
    public bool IsDhcp => Origin.Contains("Dhcp", StringComparison.OrdinalIgnoreCase);

    public string Cidr => $"{Address}/{PrefixLength}";

    /// <summary>是否为 APIPA 自动专用地址（169.254.x.x，表示网卡没拿到有效 IP）。</summary>
    public bool IsApipa
    {
        get
        {
            if (Address.AddressFamily != AddressFamily.InterNetwork) return false;
            var b = Address.GetAddressBytes();
            return b[0] == 169 && b[1] == 254;
        }
    }

    /// <summary>该地址对应网段的网络地址，例如 192.168.1.233/24 → 192.168.1.0。</summary>
    public IPAddress NetworkAddress => ComputeNetwork(Address, PrefixLength);

    public static IPAddress ComputeNetwork(IPAddress address, int prefixLength)
    {
        var b = address.GetAddressBytes();
        var n = new byte[b.Length];
        int fullBytes = prefixLength / 8;
        int remBits = prefixLength % 8;
        for (int i = 0; i < fullBytes && i < b.Length; i++) n[i] = b[i];
        if (fullBytes < b.Length && remBits > 0)
            n[fullBytes] = (byte)(b[fullBytes] & (0xFF << (8 - remBits)));
        return new IPAddress(n);
    }
}

/// <summary>网卡的聚合信息（名称、状态、地址、网关、跃点）。</summary>
public sealed class AdapterInfo
{
    public required string Name { get; init; }            // “以太网 3” / “WLAN”
    public required string Description { get; init; }     // 驱动描述，如 “Intel(R) 82576 Gigabit …”
    public required string Id { get; init; }              // {GUID}
    public required bool IsUp { get; init; }
    public required NetworkInterfaceType Type { get; init; }
    public uint? IfIndex { get; set; }                    // 路由表使用的接口索引
    public string MacAddress { get; init; } = "";
    public long SpeedBits { get; init; }                  // bps
    public List<IpAddressInfo> Ipv4Addresses { get; init; } = [];
    public List<IpAddressInfo> Ipv6Addresses { get; init; } = [];
    public List<IPAddress> GatewayV4 { get; init; } = [];
    public List<IPAddress> GatewayV6 { get; init; } = [];
    public uint? InterfaceMetricV4 { get; set; }
    public uint? InterfaceMetricV6 { get; set; }

    public string StatusText => IsUp ? "已连接" : "已断开";

    public string SpeedText => Type == NetworkInterfaceType.Loopback || !IsUp || SpeedBits <= 0
        ? "" : SpeedBits >= 1_000_000_000
        ? $"{SpeedBits / 1_000_000_000} Gbps" : $"{SpeedBits / 1_000_000} Mbps";

    /// <summary>网卡分类（用于显示与诊断启发式判断）。</summary>
    public string Kind
    {
        get
        {
            if (Type == NetworkInterfaceType.Loopback) return "环回";
            var d = Description.ToLowerInvariant();
            if (d.Contains("vmware") || d.Contains("vmnet")) return "VMware 虚拟";
            if (d.Contains("hyper-v") || d.Contains("hyper v") || Name.Contains("vEthernet", StringComparison.OrdinalIgnoreCase)) return "Hyper-V 虚拟";
            if (d.Contains("virtualbox")) return "VirtualBox 虚拟";
            if (d.Contains("plcsim")) return "PLCSIM 虚拟";
            if (d.Contains("tap") || d.Contains("tun") || d.Contains("wintun") || d.Contains("clash") || d.Contains("mihomo") || d.Contains("sing-box") || d.Contains("wireguard") || d.Contains("openvpn"))
                return "虚拟隧道(TUN)";
            if (Type == NetworkInterfaceType.Wireless80211) return "无线网卡";
            if (Type == NetworkInterfaceType.Ethernet) return "有线网卡";
            return "其他";
        }
    }

    /// <summary>是否疑似代理 / VPN 的 TUN 虚拟隧道网卡。</summary>
    public bool LooksLikeTunAdapter => Kind == "虚拟隧道(TUN)";
}
