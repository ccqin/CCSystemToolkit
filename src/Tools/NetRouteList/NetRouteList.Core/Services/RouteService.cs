using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using NetRouteList.Core.Models;
using NetRouteList.Core.Native;

namespace NetRouteList.Core.Services;

/// <summary>
/// 路由表读写服务：读取用 GetIpForwardTable2（不受系统语言影响），
/// 增删用 CreateIpForwardEntry2 / DeleteIpForwardEntry2（与 route.exe 同一底层接口）。
/// 修改路由需要管理员权限；读取不需要。
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class RouteService
{
    // ---------- 读取 ----------

    /// <summary>读取 IPv4 路由表，并填充接口名称。</summary>
    public List<RouteEntry> GetIpv4Routes(IEnumerable<AdapterInfo>? adapters = null) =>
        GetRoutes(AddressFamily.InterNetwork, adapters);

    /// <summary>读取 IPv6 路由表。</summary>
    public List<RouteEntry> GetIpv6Routes(IEnumerable<AdapterInfo>? adapters = null) =>
        GetRoutes(AddressFamily.InterNetworkV6, adapters);

    public List<RouteEntry> GetRoutes(AddressFamily family, IEnumerable<AdapterInfo>? adapters = null)
    {
        ushort af = family == AddressFamily.InterNetwork ? IpHelperNative.AF_INET : IpHelperNative.AF_INET6;
        var rows = IpHelperNative.GetForwardTable(af);

        var nameMap = adapters?.Where(a => a.IfIndex is not null)
            .ToDictionary(a => a.IfIndex!.Value, a => a.Name) ?? [];
        var metricMap = adapters?.Where(a => a.IfIndex is not null)
            .ToDictionary(
                a => a.IfIndex!.Value,
                a => family == AddressFamily.InterNetwork ? (a.InterfaceMetricV4 ?? 0) : (a.InterfaceMetricV6 ?? 0)) ?? [];

        var list = new List<RouteEntry>(rows.Count);
        foreach (var row in rows)
        {
            var dest = row.DestinationPrefix.Prefix.ToIpAddress();
            if (dest is null) continue;

            IPAddress? nextHop = null;
            if (!row.NextHop.IsOnLinkPlaceholder())
                nextHop = row.NextHop.ToIpAddress();

            nameMap.TryGetValue(row.InterfaceIndex, out var ifName);
            metricMap.TryGetValue(row.InterfaceIndex, out var ifMetric);

            list.Add(new RouteEntry
            {
                Family = family,
                Destination = IpAddressInfo.ComputeNetwork(dest, row.DestinationPrefix.PrefixLength),
                PrefixLength = row.DestinationPrefix.PrefixLength,
                NextHop = nextHop,
                InterfaceIndex = row.InterfaceIndex,
                InterfaceName = ifName,
                InterfaceMetric = ifMetric,
                Metric = row.Metric,
                Protocol = (RouteProtocol)row.Protocol,
                Origin = (RouteOrigin)row.Origin,
                IsLoopback = row.Loopback != 0,
                IsAutoconfigure = row.AutoconfigureAddress != 0,
                IsPublished = row.Publish != 0,
                IsImmortal = row.Immortal != 0,
            });
        }
        return list;
    }

    // ---------- 添加 ----------

    public RouteOpResult AddRoute(RouteMutation m)
    {
        // 基本校验
        int maxLen = m.Family == AddressFamily.InterNetwork ? 32 : 128;
        if (m.PrefixLength < 0 || m.PrefixLength > maxLen)
            return RouteOpResult.Fail($"前缀长度必须在 0 ~ {maxLen} 之间，当前为 {m.PrefixLength}。");

        if (m.Family == AddressFamily.InterNetwork && m.NextHop is not null && m.NextHop.AddressFamily != AddressFamily.InterNetwork)
            return RouteOpResult.Fail("IPv4 路由的网关也必须是 IPv4 地址。");
        if (m.Family == AddressFamily.InterNetworkV6 && m.NextHop is not null && m.NextHop.AddressFamily != AddressFamily.InterNetworkV6)
            return RouteOpResult.Fail("IPv6 路由的网关也必须是 IPv6 地址。");

        // 重复检查（比让 API 报 5010 更友好）
        var existing = GetRoutes(m.Family);
        var network = m.Network;
        if (existing.Any(r => r.Destination.Equals(network) && r.PrefixLength == m.PrefixLength
                            && r.InterfaceIndex == m.InterfaceIndex
                            && r.NextHop?.Equals(m.NextHop) == (m.NextHop is not null)
                            && r.IsOnLink == (m.NextHop is null)))
            return RouteOpResult.Fail("系统中已存在完全相同的路由条目，无需重复添加。", IpHelperNative.ERROR_OBJECT_ALREADY_EXISTS);

        var row = new Native.MIB_IPFORWARD_ROW2();
        IpHelperNative.InitializeForwardRow(ref row);

        row.InterfaceIndex = m.InterfaceIndex;
        row.DestinationPrefix = new Native.IP_ADDRESS_PREFIX
        {
            Prefix = m.Family == AddressFamily.InterNetwork
                ? Native.SOCKADDR_INET.FromV4(network)
                : Native.SOCKADDR_INET.FromV6(network),
            PrefixLength = (byte)m.PrefixLength,
        };
        if (m.NextHop is not null)
            row.NextHop = m.Family == AddressFamily.InterNetwork
                ? Native.SOCKADDR_INET.FromV4(m.NextHop)
                : Native.SOCKADDR_INET.FromV6(m.NextHop);
        else
            row.NextHop = m.Family == AddressFamily.InterNetwork
                ? new Native.SOCKADDR_INET { Family = IpHelperNative.AF_INET }
                : new Native.SOCKADDR_INET { Family = IpHelperNative.AF_INET6 };

        row.ValidLifetime = m.Persistent ? IpHelperNative.INFINITE_LIFETIME : 3600;
        row.PreferredLifetime = row.ValidLifetime;
        row.Metric = m.Metric;
        row.Protocol = (int)RouteProtocol.NetMgmt;   // 与 route add 一致：网络管理来源

        uint rc = IpHelperNative.CreateRoute(ref row);
        string cmd = RouteCommandText.Add(m);
        if (rc != IpHelperNative.NO_ERROR)
            return RouteOpResult.Fail(DescribeError(rc, "添加路由"), (int)rc, cmd);

        return RouteOpResult.Ok(m.Persistent
            ? $"已添加持久路由 {network}/{m.PrefixLength} → {m.NextHop?.ToString() ?? "在链路上"}（重启后仍生效）。"
            : $"已添加临时路由 {network}/{m.PrefixLength} → {m.NextHop?.ToString() ?? "在链路上"}（重启后失效）。", cmd);
    }

    // ---------- 删除 ----------

    public RouteOpResult DeleteRoute(RouteEntry target)
    {
        if (target.IsLoopback)
            return RouteOpResult.Fail("环回路由是系统核心路由，禁止删除。");

        if (target.IsIpv4 &&
            target.Destination.Equals(IPAddress.Loopback))
            return RouteOpResult.Fail("127.0.0.0/8 环回网段路由是系统核心路由，禁止删除。");

        // 重新查询拿到原生行，保证删除键精确匹配
        ushort af = target.IsIpv4 ? IpHelperNative.AF_INET : IpHelperNative.AF_INET6;
        var rows = IpHelperNative.GetForwardTable(af);
        Native.MIB_IPFORWARD_ROW2 match = default;
        bool found = false;
        foreach (var r in rows)
        {
            if (r.DestinationPrefix.PrefixLength == (byte)target.PrefixLength &&
                IpAddrEqual(r.DestinationPrefix.Prefix, target.Destination) &&
                r.InterfaceIndex == target.InterfaceIndex &&
                NextHopEqual(r.NextHop, target))
            {
                match = r;
                found = true;
                break;
            }
        }
        if (!found)
            return RouteOpResult.Fail("没有在当前路由表中找到这条路由（可能刚被系统或其它程序更新），请刷新后重试。", IpHelperNative.ERROR_NOT_FOUND);

        uint rc = IpHelperNative.DeleteRoute(ref match);
        string cmd = RouteCommandText.Delete(target);
        if (rc != IpHelperNative.NO_ERROR)
            return RouteOpResult.Fail(DescribeError(rc, "删除路由"), (int)rc, cmd);

        return RouteOpResult.Ok($"已删除路由 {target.DestinationCidr} → {target.GatewayText}（接口 {target.InterfaceName ?? target.InterfaceIndex.ToString()}）。", cmd);
    }

    // ---------- 修改（= 删除旧 + 添加新，失败自动回滚） ----------

    public List<RouteOpResult> UpdateRoute(RouteEntry old, RouteMutation updated)
    {
        var results = new List<RouteOpResult>();
        var del = DeleteRoute(old);
        results.Add(del);
        if (!del.Success) return results;

        var add = AddRoute(updated);
        if (!add.Success)
        {
            // 回滚：把旧路由加回去
            var rollback = AddRoute(ToMutation(old));
            results.Add(add);
            results.Add(RouteOpResult.Fail(
                rollback.Success
                    ? $"修改失败（{add.Message}）。已自动恢复原路由。"
                    : $"修改失败（{add.Message}），且自动恢复原路由也失败：{rollback.Message}。可使用备份页恢复。",
                add.Win32Error));
            return results;
        }
        results.Add(add);
        return results;
    }

    /// <summary>把现有条目转换为等效的“重新添加”参数（用于回滚/克隆）。</summary>
    public static RouteMutation ToMutation(RouteEntry r) => new()
    {
        Destination = r.Destination,
        PrefixLength = r.PrefixLength,
        NextHop = r.NextHop,
        InterfaceIndex = r.InterfaceIndex,
        Metric = r.Metric,
        Persistent = r.IsImmortal,
    };

    // ---------- 辅助 ----------

    private static bool IpAddrEqual(Native.SOCKADDR_INET a, IPAddress b)
    {
        var ip = a.ToIpAddress();
        return ip is not null && ip.Equals(b);
    }

    private static bool NextHopEqual(Native.SOCKADDR_INET nh, RouteEntry target)
    {
        if (target.IsOnLink) return nh.IsOnLinkPlaceholder();
        var ip = nh.ToIpAddress();
        return ip is not null && ip.Equals(target.NextHop);
    }

    /// <summary>把 Win32 错误码翻译成可操作的中文说明。</summary>
    public static string DescribeError(uint rc, string action) => rc switch
    {
        IpHelperNative.ERROR_ACCESS_DENIED =>
            $"{action}被拒绝：修改路由表需要管理员权限。请以管理员身份重新运行本工具（主界面左下角有提权按钮），或复制下方的等效命令在管理员命令行中手动执行。",
        IpHelperNative.ERROR_INVALID_PARAMETER =>
            $"{action}失败：参数无效。请检查目标网段、前缀长度、网关与接口是否匹配（例如网关必须与所选接口处于同一网段）。",
        IpHelperNative.ERROR_OBJECT_ALREADY_EXISTS => $"{action}失败：系统中已存在相同的路由条目。",
        IpHelperNative.ERROR_NOT_FOUND => $"{action}失败：没有找到匹配的路由条目（路由表可能已被其它程序更改），请刷新后重试。",
        _ => $"{action}失败，系统错误码 {rc}（{new Win32Exception((int)rc).Message}）。",
    };
}
