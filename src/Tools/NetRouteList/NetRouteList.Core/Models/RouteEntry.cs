using System.Net;
using System.Net.Sockets;

namespace NetRouteList.Core.Models;

/// <summary>路由来源（NL_ROUTE_ORIGIN）：这条路由最初是怎么产生的。</summary>
public enum RouteOrigin
{
    Manual = 0,
    WellKnown = 1,
    Dhcp = 2,
    RouterAdvertisement = 3,
    Isatap6to4 = 4,
    DhcpV6 = 5,
}

public static class RouteOriginExtensions
{
    public static string DisplayName(this RouteOrigin o) => o switch
    {
        RouteOrigin.Manual => "手动配置",
        RouteOrigin.WellKnown => "系统内置",
        RouteOrigin.Dhcp => "DHCP",
        RouteOrigin.RouterAdvertisement => "路由通告",
        RouteOrigin.Isatap6to4 => "ISATAP/6to4",
        RouteOrigin.DhcpV6 => "DHCPv6",
        _ => $"未知({(int)o})",
    };
}

/// <summary>
/// 一条路由的托管表示（IPv4 或 IPv6），从 GetIpForwardTable2 的原生行转换而来。
/// </summary>
public sealed class RouteEntry
{
    public required AddressFamily Family { get; init; }

    /// <summary>目标网络地址（如 192.168.1.0 / ::）。</summary>
    public required IPAddress Destination { get; init; }

    /// <summary>前缀长度（0..32 / 0..128），即“掩码有多少个 1”。</summary>
    public required int PrefixLength { get; init; }

    /// <summary>下一跳网关；为 null 表示“在链路上(on-link)”，即目标就在本网段、无需网关。</summary>
    public IPAddress? NextHop { get; init; }

    /// <summary>出接口的 ifIndex（route print 第一列的数字）。</summary>
    public required uint InterfaceIndex { get; init; }

    /// <summary>出接口名称（查询时由网卡信息填充，便于显示）。</summary>
    public string? InterfaceName { get; set; }

    /// <summary>路由跃点。route print 显示的“跃点数” = 接口跃点 + 路由跃点。</summary>
    public uint Metric { get; init; }

    /// <summary>接口跃点（查询时由网卡信息填充；环回等无数据时为 null）。</summary>
    public uint? InterfaceMetric { get; set; }

    /// <summary>总跃点 = 接口跃点 + 路由跃点，与 route print 的 Metric 列一致。</summary>
    public uint? EffectiveMetric => InterfaceMetric is null ? null : InterfaceMetric + Metric;

    public RouteProtocol Protocol { get; init; }
    public RouteOrigin Origin { get; init; }

    public bool IsLoopback { get; init; }
    public bool IsAutoconfigure { get; init; }
    public bool IsPublished { get; init; }

    /// <summary>Immortal=TRUE 表示不会过期的持久路由（route add -p）。</summary>
    public bool IsImmortal { get; init; }

    // ---------- 显示辅助 ----------
    public string DestinationCidr => $"{Destination}/{PrefixLength}";

    public bool IsDefault => PrefixLength == 0;

    public bool IsOnLink => NextHop is null || NextHop.Equals(Family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any);

    public bool IsIpv4 => Family == AddressFamily.InterNetwork;

    /// <summary>IPv4 掩码字符串（255.255.255.0 形式）；IPv6 返回 “—”。</summary>
    public string MaskText
    {
        get
        {
            if (Family != AddressFamily.InterNetwork) return "—";
            var b = new byte[4];
            int rem = PrefixLength;
            for (int i = 0; i < 4; i++)
            {
                b[i] = (byte)(rem >= 8 ? 0xFF : rem > 0 ? (0xFF << (8 - rem)) & 0xFF : 0);
                rem -= 8;
            }
            return new IPAddress(b).ToString();
        }
    }

    public string GatewayText => IsOnLink ? "在链路上" : NextHop!.ToString();

    public string KindText => (IsLoopback, IsAutoconfigure) switch
    {
        (true, _) => "环回",
        (_, true) => "自动配置",
        (_, false) when IsImmortal => "持久路由",
        _ => "动态",
    };

    public override string ToString() =>
        $"{DestinationCidr} → {GatewayText} (if {InterfaceIndex}, metric {Metric}, {Origin.DisplayName()})";

    /// <summary>路由的业务键：目标前缀 + 下一跳 + 接口，用于备份比对与去重。</summary>
    public string Key => $"{Destination}/{PrefixLength}|{(IsOnLink ? "onlink" : NextHop!.ToString())}|{InterfaceIndex}";
}
