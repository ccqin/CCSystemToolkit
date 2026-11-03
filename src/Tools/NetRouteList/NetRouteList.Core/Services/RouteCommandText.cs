using System.Net;
using System.Net.Sockets;
using NetRouteList.Core.Models;

namespace NetRouteList.Core.Services;

/// <summary>
/// 生成与图形操作等效的命令行命令（route.exe / netsh），
/// 供界面展示与教学：理解每一步操作在系统层面意味着什么。
/// </summary>
public static class RouteCommandText
{
    /// <summary>添加路由的等效 route.exe 命令。</summary>
    public static string Add(RouteMutation m)
    {
        bool v4 = m.Family == AddressFamily.InterNetwork;
        string dest = m.Network.ToString();
        string gw = m.NextHop?.ToString() ?? (v4 ? "0.0.0.0" : "::");
        string p = m.Persistent ? "-p " : "";
        if (v4)
        {
            uint mask = m.PrefixLength == 0 ? 0 : (m.PrefixLength == 32 ? 0xFFFFFFFFu : ~(0xFFFFFFFFu >> m.PrefixLength));
            return $"route add {p}{dest} mask {new IPAddress(mask)} {gw} metric {m.Metric} if {m.InterfaceIndex}";
        }
        return $"netsh interface ipv6 add route {dest}/{m.PrefixLength} interface={m.InterfaceIndex} nexthop={gw} metric={m.Metric}" +
               (m.Persistent ? " publish=yes" : " store=active");
    }

    /// <summary>删除路由的等效 route.exe 命令。</summary>
    public static string Delete(RouteEntry r)
    {
        if (r.IsIpv4)
        {
            return r.IsOnLink
                ? $"route delete {r.Destination} mask {r.MaskText} 0.0.0.0 if {r.InterfaceIndex}"
                : $"route delete {r.Destination} mask {r.MaskText} {r.NextHop}";
        }
        return r.IsOnLink
            ? $"netsh interface ipv6 delete route {r.Destination}/{r.PrefixLength} interface={r.InterfaceIndex}"
            : $"netsh interface ipv6 delete route {r.Destination}/{r.PrefixLength} {r.NextHop}";
    }

    /// <summary>把现有路由打印成 route print 风格的一行。</summary>
    public static string PrintStyle(RouteEntry r) =>
        r.IsIpv4
            ? $"{r.Destination,-16} {r.MaskText,-16} {r.GatewayText,-16} {r.InterfaceIndex,-4} {r.Metric}"
            : $"{r.DestinationCidr,-44} {r.GatewayText,-30} {r.InterfaceIndex,-4} {r.Metric}";
}
