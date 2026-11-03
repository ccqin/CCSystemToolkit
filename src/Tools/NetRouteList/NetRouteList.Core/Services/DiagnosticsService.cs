using System.Net;
using NetRouteList.Core.Models;

namespace NetRouteList.Core.Services;

/// <summary>
/// 路由健康诊断：自动检测常见网络问题 —— 多默认路由、缺失 on-link 直连路由、
/// APIPA 地址、跨网卡网段冲突、代理隧道(TUN)接管 等，并尽可能给出解释与一键修复。
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class DiagnosticsService
{
    public sealed class Report
    {
        public required List<AdapterInfo> Adapters { get; init; }
        public required List<RouteEntry> RoutesV4 { get; init; }
        public required List<DiagnosticIssue> Issues { get; init; }

        public int AdapterCount => Adapters.Count;
        public int RouteCountV4 => RoutesV4.Count;
        public int DefaultRouteCount => RoutesV4.Count(r => r.IsDefault);
        public int MissingOnLinkCount => Issues.FirstOrDefault(i => i.Repair?.Title.Contains("on-link") == true)?.Affected.Count ?? 0;
        public int ErrorCount => Issues.Count(i => i.Severity == IssueSeverity.Error);
        public int WarningCount => Issues.Count(i => i.Severity == IssueSeverity.Warning);
    }

    private readonly RouteService _routes;

    public DiagnosticsService(RouteService routes) => _routes = routes;

    public Report Analyze(List<AdapterInfo>? adapters = null, List<RouteEntry>? routesV4 = null)
    {
        adapters ??= new AdapterService().GetAdapters();
        routesV4 ??= _routes.GetIpv4Routes(adapters);

        var issues = new List<DiagnosticIssue>();
        CheckDefaultRoutes(routesV4, issues);
        CheckMissingOnLink(adapters, routesV4, issues);
        CheckOverlappingSubnets(adapters, issues);
        CheckApipa(adapters, issues);
        CheckTunTakeover(adapters, routesV4, issues);

        return new Report { Adapters = adapters, RoutesV4 = routesV4, Issues = issues };
    }

    // ---------- 规则 1：多条默认路由 ----------
    private static void CheckDefaultRoutes(List<RouteEntry> routes, List<DiagnosticIssue> issues)
    {
        var defaults = routes.Where(r => r.IsDefault).OrderBy(r => r.EffectiveMetric ?? r.Metric).ToList();
        if (defaults.Count <= 1)
        {
            issues.Add(new DiagnosticIssue
            {
                Severity = IssueSeverity.Info,
                Title = "默认路由正常",
                Explanation = "默认路由(0.0.0.0/0)是“其它路由都不匹配时的兜底出口”，也就是通常说的“上网出口”。当前只有一条，目的地不明的流量会统一走这条默认路由，行为可预期。",
                Suggestion = "无需处理。",
            });
            return;
        }

        var best = defaults[0];
        issues.Add(new DiagnosticIssue
        {
            Severity = IssueSeverity.Warning,
            Title = $"存在 {defaults.Count} 条默认路由",
            Explanation = "默认路由决定“目的地不明的流量走哪里”。有多条时系统并非随机选择，而是选“总跃点(接口跃点+路由跃点)”最小的一条；其余的只在主路由失效时作为备份。需要注意的是：VPN/代理的虚拟网卡(TUN)常以极低跃点插入默认路由，从而把全部流量吸进隧道 —— 这本身可能是你想要的效果，但当你发现某些流量“莫名走了代理”时，先看这里。",
            Affected = defaults.Select(r =>
                $"0.0.0.0/0 → {r.GatewayText}，接口 {r.InterfaceName ?? r.InterfaceIndex.ToString()}，跃点 {r.EffectiveMetric ?? r.Metric}{((r.EffectiveMetric ?? r.Metric) == (best.EffectiveMetric ?? best.Metric) ? "（当前生效/并列最优）" : "")}").ToList(),
            Suggestion = $"当前最优出口：接口 {best.InterfaceName ?? best.InterfaceIndex.ToString()}（跃点 {best.EffectiveMetric ?? best.Metric}）。若想更换上网出口，可调低目标网卡的路由跃点、调高其它默认路由的跃点，或删除多余的默认路由。",
        });
    }

    // ---------- 规则 2：缺失 on-link 直连路由（核心检测） ----------
    private void CheckMissingOnLink(List<AdapterInfo> adapters, List<RouteEntry> routes, List<DiagnosticIssue> issues)
    {
        var missing = new List<(AdapterInfo Adapter, IpAddressInfo Ip)>();

        foreach (var ad in adapters.Where(a => a.IsUp && a.IfIndex is not null))
        {
            foreach (var ip in ad.Ipv4Addresses)
            {
                if (ip.IsApipa) continue; // APIPA 没有直连路由意义
                var network = ip.NetworkAddress;
                bool has = routes.Any(r =>
                    r.IsIpv4 && r.IsOnLink &&
                    r.InterfaceIndex == ad.IfIndex!.Value &&
                    r.PrefixLength == ip.PrefixLength &&
                    r.Destination.Equals(network));
                if (!has) missing.Add((ad, ip));
            }
        }

        if (missing.Count == 0)
        {
            issues.Add(new DiagnosticIssue
            {
                Severity = IssueSeverity.Info,
                Title = "直连(on-link)路由完整",
                Explanation = "每给网卡配置一个 IP，系统应自动生成一条对应的直连路由（例如 192.168.1.0/24 → 在链路上）。它告诉系统“这个网段的设备就接在本网卡上，直接发 ARP 送达，不需要网关”。缺失直连路由是内网不通的最常见根因。",
                Suggestion = "无需处理。",
            });
            return;
        }

        var affected = missing.Select(m =>
            $"{m.Adapter.Name}（if {m.Adapter.IfIndex}）：{m.Ip.Cidr} 缺少 {m.Ip.NetworkAddress}/{m.Ip.PrefixLength} 的 on-link 路由").ToList();

        var bestDefault = routes.Where(r => r.IsDefault).OrderBy(r => r.EffectiveMetric ?? r.Metric).FirstOrDefault();
        string consequence = bestDefault is not null
            ? $"后果：发往这些网段的流量会掉进默认路由（当前最优默认路由走接口 {bestDefault.InterfaceName ?? bestDefault.InterfaceIndex.ToString()}，跃点 {bestDefault.EffectiveMetric ?? bestDefault.Metric}）。如果那是一条代理/VPN 隧道的默认路由，这些内网流量就会全部被塞进代理 —— 这正是“挂了代理后内网设备 Ping 不通、页面打不开”的典型成因。"
            : "后果：发往这些网段的流量没有路由可用，直接不可达。";

        List<RouteMutation> Build(bool persistent) => missing.Select(m => new RouteMutation
        {
            Destination = m.Ip.NetworkAddress,
            PrefixLength = m.Ip.PrefixLength,
            NextHop = null, // on-link
            InterfaceIndex = m.Adapter.IfIndex!.Value,
            Metric = 1,
            Persistent = persistent,
        }).ToList();

        issues.Add(new DiagnosticIssue
        {
            Severity = IssueSeverity.Error,
            Title = $"缺失 {missing.Count} 条直连(on-link)路由",
            Explanation = "直连路由(on-link)表示“目标网段就接在这块网卡上，无需网关、直接送达”。缺失它，去该网段的流量不会从这块网卡直发。" + consequence,
            Affected = affected,
            Suggestion = "点击下方“一键修复”为每个缺失网段补回 on-link 路由（等价于 route add 网段 mask 掩码 0.0.0.0 if 接口）；确认框里可勾选“持久路由”，勾选后重启仍生效。修复后建议断开重连代理软件验证效果。",
            Repair = new RepairAction
            {
                Title = "一键修复缺失的 on-link 路由",
                ConfirmText = $"将为 {missing.Count} 个网段各添加一条直连(on-link)路由（路由跃点=1）。\n这些路由只影响指定网段的走法，不会改变上网出口，可随时在“路由表”页删除。",
                OptionText = "持久路由（route add -p，写入注册表，重启后仍生效；不勾则重启后失效）",
                OptionDefault = true,
                Preview = persistent => Build(persistent).Select(RouteCommandText.Add).ToList(),
                Execute = persistent => Build(persistent).Select(m => _routes.AddRoute(m)).ToList(),
            },
        });
    }

    // ---------- 规则 3：跨网卡网段冲突 ----------
    private static void CheckOverlappingSubnets(List<AdapterInfo> adapters, List<DiagnosticIssue> issues)
    {
        var groups = new List<string>();
        var flagged = new HashSet<int>();

        var ipList = adapters
            .Where(a => a.IfIndex is not null)
            .SelectMany(a => a.Ipv4Addresses.Where(ip => !ip.IsApipa).Select(ip => (a, ip)))
            .ToList();

        for (int i = 0; i < ipList.Count; i++)
        {
            for (int j = i + 1; j < ipList.Count; j++)
            {
                var (a1, ip1) = ipList[i];
                var (a2, ip2) = ipList[j];
                if (a1.IfIndex == a2.IfIndex) continue;
                if (flagged.Contains(i) && flagged.Contains(j)) continue;

                // 前缀包含关系判定
                int minLen = Math.Min(ip1.PrefixLength, ip2.PrefixLength);
                var n1 = IpAddressInfo.ComputeNetwork(ip1.Address, minLen);
                var n2 = IpAddressInfo.ComputeNetwork(ip2.Address, minLen);
                if (n1.Equals(n2))
                {
                    var overlap = $"{IpAddressInfo.ComputeNetwork(ip1.Address, minLen)}/{minLen}";
                    groups.Add($"网卡“{a1.Name}”(if {a1.IfIndex}) 的 {ip1.Cidr}  ↔  网卡“{a2.Name}”(if {a2.IfIndex}) 的 {ip2.Cidr}    →  重叠网段：{overlap}");
                    flagged.Add(i); flagged.Add(j);
                }
            }
        }

        if (groups.Count > 0)
        {
            issues.Add(new DiagnosticIssue
            {
                Severity = IssueSeverity.Warning,
                Title = $"{groups.Count} 组网卡之间存在网段冲突",
                Explanation = "两块网卡配置了相同（或互相包含）的网段时，系统只能按跃点把该网段流量交给其中一块网卡，另一块网卡上的同网段设备可能永远无法访问；某些软件（尤其是虚拟机、工控 PLC 工具）会出现“时通时断”的诡异表现。这是工业现场多网卡机器最常见的配置错误。",
                Affected = groups,
                Suggestion = "对照上方明细：保留需要通信的那块网卡上的 IP，移除或修改另一块网卡上冲突的 IP，使每块网卡的网段互不重叠；若确实无法避免（如两套产线都用 192.168.1.x），可用更具体前缀的路由（/25、/26…）或调整跃点来明确流量走向。",
            });
        }
    }

    // ---------- 规则 4：APIPA 地址 ----------
    private static void CheckApipa(List<AdapterInfo> adapters, List<DiagnosticIssue> issues)
    {
        var apipa = adapters.Where(a => a.Ipv4Addresses.Count > 0 && a.Ipv4Addresses.All(ip => ip.IsApipa)).ToList();
        if (apipa.Count == 0) return;

        issues.Add(new DiagnosticIssue
        {
            Severity = IssueSeverity.Info,
            Title = $"{apipa.Count} 块网卡只拿到 169.254.x.x 自动地址(APIPA)",
            Explanation = "169.254.0.0/16 是 Windows 在“网卡连通了但 DHCP 服务器没回应”时自配的临时地址，表示这块网卡没有拿到有效 IP。它不能正常通信（除链路本地邻居），仅说明“物理连上了、逻辑没通”。",
            Affected = apipa.Select(a => $"{a.Name}（{a.StatusText}）：{string.Join(", ", a.Ipv4Addresses.Select(ip => ip.Cidr))}").ToList(),
            Suggestion = "若这块网卡本应上网：检查网线/交换机/DHCP 服务器，或执行 ipconfig /renew。若它只用于直连设备（如 PLC 直连），请为它手动配置静态 IP。",
        });
    }

    // ---------- 规则 5：代理/VPN 隧道(TUN)接管提示 ----------
    private static void CheckTunTakeover(List<AdapterInfo> adapters, List<RouteEntry> routes, List<DiagnosticIssue> issues)
    {
        var tuns = adapters.Where(a => a.LooksLikeTunAdapter && a.IfIndex is not null).ToList();
        if (tuns.Count == 0) return;

        var lines = new List<string>();
        foreach (var tun in tuns)
        {
            var defaults = routes.Where(r => r.IsDefault && r.InterfaceIndex == tun.IfIndex).ToList();
            if (defaults.Count > 0)
            {
                lines.Add($"{tun.Name}：持有默认路由 0.0.0.0/0 → {string.Join("/", tun.GatewayV4.Select(g => g.ToString()))}，跃点 {defaults.Min(r => r.EffectiveMetric ?? r.Metric)}");
            }
            else
            {
                var tunRoutes = routes.Count(r => r.InterfaceIndex == tun.IfIndex);
                lines.Add($"{tun.Name}：无默认路由（当前未接管全局流量，承载 {tunRoutes} 条路由）");
            }
        }

        if (lines.Count == 0) return;

        issues.Add(new DiagnosticIssue
        {
            Severity = IssueSeverity.Info,
            Title = "检测到代理/VPN 虚拟隧道网卡(TUN)",
            Explanation = "Clash/Mihomo/WireGuard 等代理或 VPN 软件会创建虚拟网卡(TUN)，并常以“跃点 0/1 的默认路由”接管全部流量 —— 所有未匹配到更具体路由的流量都会进入隧道。当内网直连路由缺失或跃点过高时，就会出现“内网流量也被代理”的问题。若在总览看到“缺失 on-link 路由”的错误，优先修复它。",
            Affected = lines,
            Suggestion = "这是代理软件的正常工作方式，通常无需处理。若内网流量被误代理：① 修复缺失的直连路由；② 在代理软件里配置绕过规则（如 192.168.0.0/16 direct）；③ 或调高该 TUN 默认路由的跃点。",
        });
    }
}
