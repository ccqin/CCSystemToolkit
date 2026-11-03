namespace NetRouteList.Core.Models;

/// <summary>
/// 路由的“协议来源”，表示这条路由是系统怎么学来的。
/// 对应 netioapi.h 中的 NL_ROUTE_PROTOCOL / MIB_IPPROTO。
/// </summary>
public enum RouteProtocol
{
    Other = 1,
    /// <summary>接口自身生成（通常是直连/on-link 路由）。</summary>
    Local = 2,
    /// <summary>网络管理手段添加 —— 手动执行 route add / 本工具添加的都是这一类。</summary>
    NetMgmt = 3,
    /// <summary>ICMP 重定向学到的临时路由。</summary>
    Icmp = 4,
    Egp = 5,
    Ggp = 6,
    Hello = 7,
    Rip = 8,
    IsIs = 9,
    EsIs = 10,
    Cisco = 11,
    Bbn = 12,
    Ospf = 13,
    Bgp = 14,
    /// <summary>DHCP 服务器下发（常见于默认网关路由）。</summary>
    Dhcp = 10004,
    NtAutoStatic = 10002,
    NtStatic = 10006,
    NtStaticNonDod = 10007,
}

public static class RouteProtocolExtensions
{
    /// <summary>协议的中文显示名（带解释性短语，供表格直接显示）。</summary>
    public static string DisplayName(this RouteProtocol p) => p switch
    {
        RouteProtocol.Other => "其他",
        RouteProtocol.Local => "本地 (接口生成)",
        RouteProtocol.NetMgmt => "手动 (route add)",
        RouteProtocol.Icmp => "ICMP 重定向",
        RouteProtocol.Egp => "EGP",
        RouteProtocol.Ggp => "GGP",
        RouteProtocol.Hello => "Hello",
        RouteProtocol.Rip => "RIP",
        RouteProtocol.IsIs => "IS-IS",
        RouteProtocol.EsIs => "ES-IS",
        RouteProtocol.Cisco => "Cisco",
        RouteProtocol.Bbn => "BBN",
        RouteProtocol.Ospf => "OSPF",
        RouteProtocol.Bgp => "BGP",
        RouteProtocol.Dhcp => "DHCP 下发",
        RouteProtocol.NtAutoStatic => "自动静态",
        RouteProtocol.NtStatic => "静态",
        RouteProtocol.NtStaticNonDod => "静态(非 DOD)",
        _ => $"未知 ({(int)p})",
    };

    /// <summary>协议的详细解释（供工具提示 / 帮助页使用）。</summary>
    public static string Description(this RouteProtocol p) => p switch
    {
        RouteProtocol.Local => "由网络接口自身生成：给接口配置 IP 地址后，系统自动为该网段创建的直连（on-link）路由。删除后通常会被系统重建。",
        RouteProtocol.NetMgmt => "由管理动作创建：手动执行 route add / netsh add route，或使用本工具添加的路由。这是用户可完全控制的类型。",
        RouteProtocol.Dhcp => "由 DHCP 服务器下发，典型是默认网关(0.0.0.0/0)路由。IP 租约更新或网卡重连时会被刷新。",
        RouteProtocol.Icmp => "由 ICMP 重定向报文临时学习到的路由，一般是网络中路由器指路的产物，会自动过期。",
        RouteProtocol.NtAutoStatic => "由拨号/路由协议请求的自动静态路由。",
        RouteProtocol.NtStatic => "netsh 添加的静态路由，存于注册表，重启后保留。",
        RouteProtocol.NtStaticNonDod => "非按需拨号的静态路由。",
        _ => "路由协议来源。此处为不常见或第三方协议产生。 manual 添加的 NetMgmt 与系统自动生成(Local/DHCP) 是日常最常见的两类。",
    };
}
