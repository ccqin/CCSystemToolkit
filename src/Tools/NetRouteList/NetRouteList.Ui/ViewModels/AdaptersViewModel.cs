using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetRouteList.Core.Models;

namespace NetRouteList.Ui.ViewModels;

/// <summary>网卡单个 IP 的显示项。</summary>
public partial class AdapterIpViewModel : ObservableObject
{
    public required IpAddressInfo Ip { get; init; }
    public required bool HasOnLinkRoute { get; init; }

    public string Text => Ip.Cidr + (Ip.IsDhcp ? "（DHCP）" : "（静态）");

    /// <summary>APIPA 地址：系统只为其中一块网卡挂 169.254/16 路由，其它网卡上没有属正常现象，不告警。</summary>
    public bool IsApipa => Ip.IsApipa;

    public string OnLinkText => IsApipa ? "APIPA 自动地址" : HasOnLinkRoute ? "有直连路由" : "缺直连路由!";
    public bool Missing => !IsApipa && !HasOnLinkRoute;
    public string ToolTip => IsApipa
        ? $"{Ip.Cidr}\nAPIPA（169.254.x.x）自动地址：网卡连通但没拿到有效 IP。系统只在一块网卡上维护 169.254/16 路由，属正常现象。"
        : HasOnLinkRoute
        ? $"{Ip.Cidr}\n该网段存在对应的“在链路上(on-link)”路由，同网段设备可直接通信。"
        : $"{Ip.Cidr}\n该网段缺少 on-link 直连路由！发往 {Ip.NetworkAddress}/{Ip.PrefixLength} 的流量会掉进默认路由（很可能被代理隧道抢走）。请到“总览”页一键修复。";
}

/// <summary>网卡条目的显示包装。</summary>
public partial class AdapterRowViewModel : ObservableObject
{
    public required AdapterInfo Info { get; init; }
    public required ObservableCollection<AdapterIpViewModel> Ipv4Items { get; init; }

    public string Name => Info.Name;
    public string Kind => Info.Kind;
    public string Status => Info.IsUp ? $"已连接 {Info.SpeedText}".Trim() : "已断开";
    public string StatusColor => Info.IsUp ? "#107C10" : "#8A8886";
    public string IfIndex => Info.IfIndex?.ToString() ?? "—";
    public string Mac => string.IsNullOrEmpty(Info.MacAddress) ? "—" :
        string.Join(":", Enumerable.Range(0, 6).Select(i => Info.MacAddress.Substring(i * 2, 2)));
    public string Metric => Info.InterfaceMetricV4?.ToString() ?? "—";
    public string Ips => Ipv4Items.Count == 0 ? "—" : string.Join("\n", Ipv4Items.Select(i => i.Text));
    public string Gateways => Info.GatewayV4.Count == 0 ? "无" : string.Join("\n", Info.GatewayV4);
    public bool HasWarning => Ipv4Items.Any(i => i.Missing);

    public string ToolTip =>
        $"{Info.Name}（{Info.Kind}）\n{Info.Description}\n" +
        $"接口索引 {IfIndex} · 接口跃点 {Metric}\n" +
        $"IPv4：{(Ips == "—" ? "无" : Ips)}\n网关：{Gateways}\n" +
        $"接口跃点越小，该网卡上的路由总跃点越低、越容易被优先选中。";
}

/// <summary>网卡页。</summary>
public partial class AdaptersViewModel : PageViewModelBase
{
    public override string Title => "网卡";
    public override string Glyph => "▤";
    public override string NavTip => "所有网卡的名称、类型、状态、IP、网关、接口跃点，并标注每个 IP 是否有对应直连路由。";

    public ObservableCollection<AdapterRowViewModel> Adapters { get; } = [];

    public override void ApplySnapshot(SystemSnapshot snap)
    {
        base.ApplySnapshot(snap);
        Adapters.Clear();
        foreach (var a in snap.Adapters)
        {
            var items = new ObservableCollection<AdapterIpViewModel>(
                a.Ipv4Addresses.Select(ip => new AdapterIpViewModel
                {
                    Ip = ip,
                    HasOnLinkRoute = snap.RoutesV4.Any(r =>
                        r.IsOnLink && r.InterfaceIndex == a.IfIndex &&
                        r.PrefixLength == ip.PrefixLength &&
                        r.Destination.Equals(ip.NetworkAddress)),
                }));
            Adapters.Add(new AdapterRowViewModel { Info = a, Ipv4Items = items });
        }
    }

    [RelayCommand]
    private void Refresh() => Main.RefreshAll();

    /// <summary>跳到路由表页并按该网卡筛选（View 层订阅执行导航）。</summary>
    public event Action<AdapterRowViewModel>? ViewRoutesRequested;

    [RelayCommand]
    private void ViewRoutes(object param)
    {
        if (param is AdapterRowViewModel row) ViewRoutesRequested?.Invoke(row);
    }
}
