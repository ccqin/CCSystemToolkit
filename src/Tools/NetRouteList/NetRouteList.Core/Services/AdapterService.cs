using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NetRouteList.Core.Models;
using NetRouteList.Core.Native;

namespace NetRouteList.Core.Services;

/// <summary>
/// 网卡信息聚合：托管 NetworkInformation API 取名称/地址/网关，
/// P/Invoke GetIpInterfaceEntry 补充接口跃点（托管 API 不提供）。
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class AdapterService
{
    public List<AdapterInfo> GetAdapters()
    {
        var result = new List<AdapterInfo>();
        // 过滤驱动伪接口（Npcap/WFP/QoS 等以 “-0000” 形式挂在真实网卡名后面），它们不是可配置的网卡
        var pseudoPattern = new System.Text.RegularExpressions.Regex(@"-\d{4}$");
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => !pseudoPattern.IsMatch(n.Name)))
        {
            var props = nic.GetIPProperties();
            uint? ifIndex = null;
            try { if (props.GetIPv4Properties() is { } v4p) ifIndex = (uint)v4p.Index; } catch { /* 无 IPv4 栈 */ }
            if (ifIndex is null)
                ifIndex = TryGetV6Index(props);

            var v4 = new List<IpAddressInfo>();
            var v6 = new List<IpAddressInfo>();
            foreach (var ua in props.UnicastAddresses)
            {
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    v4.Add(new IpAddressInfo
                    {
                        Address = ua.Address,
                        PrefixLength = ua.PrefixLength,
                        Origin = ua.PrefixOrigin.ToString(),
                    });
                }
                else if (ua.Address.AddressFamily == AddressFamily.InterNetworkV6 && !ua.Address.IsIPv6LinkLocal)
                {
                    v6.Add(new IpAddressInfo
                    {
                        Address = ua.Address,
                        PrefixLength = ua.PrefixLength,
                        Origin = ua.PrefixOrigin.ToString(),
                    });
                }
            }

            var info = new AdapterInfo
            {
                Name = nic.Name,
                Description = nic.Description,
                Id = nic.Id,
                IsUp = nic.OperationalStatus == OperationalStatus.Up,
                Type = nic.NetworkInterfaceType,
                IfIndex = ifIndex,
                MacAddress = nic.GetPhysicalAddress().ToString(),
                SpeedBits = nic.Speed,
                Ipv4Addresses = v4,
                Ipv6Addresses = v6,
                GatewayV4 = props.GatewayAddresses.Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork).Select(g => g.Address).ToList(),
                GatewayV6 = props.GatewayAddresses.Where(g => g.Address.AddressFamily == AddressFamily.InterNetworkV6).Select(g => g.Address).ToList(),
            };

            // 接口跃点（v4/v6 各一份）
            if (ifIndex is not null)
            {
                if (IpHelperNative.QueryIpInterface(IpHelperNative.AF_INET, ifIndex.Value, out var rowV4) == IpHelperNative.NO_ERROR)
                    info.InterfaceMetricV4 = rowV4.Metric;
                if (IpHelperNative.QueryIpInterface(IpHelperNative.AF_INET6, ifIndex.Value, out var rowV6) == IpHelperNative.NO_ERROR)
                    info.InterfaceMetricV6 = rowV6.Metric;
            }

            result.Add(info);
        }
        return result;
    }

    private static uint? TryGetV6Index(IPInterfaceProperties props)
    {
        try { if (props.GetIPv6Properties() is { } v6p) return (uint)v6p.Index; } catch { }
        return null;
    }
}
