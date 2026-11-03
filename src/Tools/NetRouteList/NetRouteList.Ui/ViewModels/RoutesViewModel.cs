using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetRouteList.Core.Models;
using NetRouteList.Core.Services;

namespace NetRouteList.Ui.ViewModels;

/// <summary>DataGrid 一行的显示包装。</summary>
public partial class RouteRowViewModel : ObservableObject
{
    public required RouteEntry Entry { get; init; }

    public string Destination => Entry.Destination.ToString();
    public string Cidr => Entry.DestinationCidr;
    public string Mask => Entry.MaskText;
    public string Prefix => "/" + Entry.PrefixLength;
    public string Gateway => Entry.GatewayText;
    public string Interface => $"{Entry.InterfaceName ?? "?"} ({Entry.InterfaceIndex})";
    public string MetricText => Entry.EffectiveMetric is uint eff
        ? $"{eff}（接口{Entry.InterfaceMetric}+路由{Entry.Metric}）"
        : Entry.Metric.ToString();
    public string Protocol => Entry.Protocol.DisplayName();
    public string Origin => Entry.Origin.DisplayName();
    public string Kind => Entry.KindText;
    public bool IsDefault => Entry.IsDefault;
    public bool IsOnLink => Entry.IsOnLink;
    public string DeleteCommandPreview => RouteCommandText.Delete(Entry);
    public string AddCommandPreview => RouteCommandText.Add(RouteService.ToMutation(Entry));

    public string ToolTipText =>
        $"目标 {Cidr}（掩码 {Mask}）\n下一跳：{Gateway}\n出接口：{Interface}\n" +
        $"总跃点：{MetricText}\n协议来源：{Protocol}（{Entry.Protocol.Description()}）\n" +
        $"路由来源：{Origin}\n类型：{Kind}\n" +
        (Entry.IsImmortal ? "持久路由：重启后仍生效（route add -p）" : "临时路由：重启后失效");
}

/// <summary>路由表页：列表、筛选、增删改。</summary>
public partial class RoutesViewModel : PageViewModelBase
{
    public override string Title => "路由表";
    public override string Glyph => "⇄";
    public override string NavTip => "查看/筛选/搜索全部 IPv4 与 IPv6 路由，添加、修改、删除路由，每列悬停有字段说明。";

    public ObservableCollection<RouteRowViewModel> All { get; } = [];
    public ObservableCollection<RouteRowViewModel> Visible { get; } = [];

    /// <summary>接口筛选选项：“全部” + 各网卡名。</summary>
    public ObservableCollection<string> InterfaceOptions { get; } = ["全部接口"];

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private int _familyIndex; // 0=全部 1=IPv4 2=IPv6
    [ObservableProperty] private string _interfaceFilter = "全部接口";
    [ObservableProperty] private RouteRowViewModel? _selected;
    [ObservableProperty] private string _summaryText = "尚未刷新";

    public bool CanModify => Main.IsElevated;

    public override void ApplySnapshot(SystemSnapshot snap)
    {
        base.ApplySnapshot(snap);
        All.Clear();
        foreach (var r in snap.RoutesV4.Concat(snap.RoutesV6)
                     .OrderBy(r => r.IsIpv4 ? 4 : 6)
                     .ThenBy(r => r.Destination.ToString())
                     .ThenBy(r => r.PrefixLength))
            All.Add(new RouteRowViewModel { Entry = r });

        var keepIf = InterfaceFilter;
        InterfaceOptions.Clear();
        InterfaceOptions.Add("全部接口");
        foreach (var name in snap.Adapters.Where(a => a.IfIndex is not null).Select(a => a.Name).Distinct())
            InterfaceOptions.Add(name);
        InterfaceFilter = InterfaceOptions.Contains(keepIf) ? keepIf : "全部接口";

        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnFamilyIndexChanged(int value) => ApplyFilter();
    partial void OnInterfaceFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Visible.Clear();
        string q = SearchText.Trim();
        foreach (var r in All)
        {
            if (FamilyIndex == 1 && !r.Entry.IsIpv4) continue;
            if (FamilyIndex == 2 && r.Entry.IsIpv4) continue;
            if (InterfaceFilter != "全部接口" && r.Entry.InterfaceName != InterfaceFilter) continue;
            if (q.Length > 0 &&
                !r.Cidr.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !r.Gateway.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !r.Interface.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            Visible.Add(r);
        }
        SummaryText = $"共 {Visible.Count} / {All.Count} 条" +
            (All.Count > 0 ? $" · 默认路由 {All.Count(r => r.Entry.IsDefault)} 条" : "");
    }

    [RelayCommand]
    private void Refresh() => Main.RefreshAll();

    /// <summary>弹出添加/编辑对话框；由 View 层订阅执行。</summary>
    public event Action<RouteRowViewModel?>? EditRequested; // null=新增
    [RelayCommand]
    private void AddRoute() => EditRequested?.Invoke(null);
    [RelayCommand]
    private void EditRoute() => EditRequested?.Invoke(Selected);

    /// <summary>删除当前选中（View 层先弹确认框）。</summary>
    public event Action<RouteRowViewModel>? DeleteRequested;
    [RelayCommand]
    private void DeleteRoute()
    {
        if (Selected is { } sel) DeleteRequested?.Invoke(sel);
    }

    public void ApplyMutationResult(IEnumerable<RouteOpResult> results)
    {
        foreach (var r in results)
        {
            Main.AddLog((r.Success ? "✔ " : "✘ ") + r.Message + (r.EquivalentCommand is { } c ? $"  [{c}]" : ""));
        }
        Main.StatusText = results.LastOrDefault()?.Message ?? "";
        Refresh();
    }
}

/// <summary>添加/编辑路由对话框的 ViewModel。</summary>
public partial class RouteEditViewModel : ObservableObject
{
    /// <summary>null = 新增；否则编辑已有条目（允许改跃点/网关/接口）。</summary>
    public RouteRowViewModel? Original { get; }

    public ObservableCollection<AdapterInfo> Adapters { get; }
    public bool IsEdit => Original is not null;

    [ObservableProperty] private string _destinationText = "";
    [ObservableProperty] private int _prefixLength = 24;
    [ObservableProperty] private bool _isOnLink = true;
    [ObservableProperty] private string _gatewayText = "";
    [ObservableProperty] private AdapterInfo? _selectedAdapter;
    [ObservableProperty] private string _metricText = "1";
    [ObservableProperty] private bool _persistent;
    [ObservableProperty] private string _errorText = "";

    public string Title => IsEdit ? "修改路由" : "添加路由";

    public RouteEditViewModel(ObservableCollection<AdapterInfo> adapters, RouteRowViewModel? original)
    {
        Adapters = adapters;
        Original = original;
        if (original?.Entry is { } e)
        {
            DestinationText = e.Destination.ToString();
            PrefixLength = e.PrefixLength;
            IsOnLink = e.IsOnLink;
            GatewayText = e.IsOnLink ? "" : e.NextHop!.ToString();
            MetricText = e.Metric.ToString();
            Persistent = e.IsImmortal;
            SelectedAdapter = adapters.FirstOrDefault(a => a.IfIndex == e.InterfaceIndex)
                              ?? adapters.FirstOrDefault(a => a.Name == e.InterfaceName);
        }
        else
        {
            SelectedAdapter = adapters.FirstOrDefault(a => a.IsUp && a.IfIndex is not null);
        }
    }

    public string PreviewText => BuildMutation() is { } m
        ? "将执行（等效命令）：\n" + RouteCommandText.Add(m)
        : "填写完整后这里会显示等效的 route 命令。";

    partial void OnDestinationTextChanged(string value) => ErrorText = "";
    partial void OnGatewayTextChanged(string value) => ErrorText = "";

    /// <summary>校验并构造 RouteMutation；失败返回 null 且写入 ErrorText。</summary>
    public RouteMutation? BuildMutation()
    {
        if (!IPAddress.TryParse(DestinationText.Trim(), out var dest))
        { ErrorText = "目标网络不是有效的 IP 地址（填网络地址或该网段任一地址均可，如 192.168.1.0）。"; return null; }
        int maxLen = dest.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        if (PrefixLength < 0 || PrefixLength > maxLen)
        { ErrorText = $"前缀长度需在 0~{maxLen} 之间。"; return null; }

        IPAddress? gw = null;
        if (!IsOnLink)
        {
            if (!IPAddress.TryParse(GatewayText.Trim(), out gw))
            { ErrorText = "网关不是有效的 IP 地址；若目标就在本网卡网段内，请改选“在链路上”。"; return null; }
            if (gw.AddressFamily != dest.AddressFamily)
            { ErrorText = "网关地址族与目标不一致（IPv4 目标配 IPv4 网关）。"; return null; }
        }

        var adapter = SelectedAdapter;
        if (adapter?.IfIndex is not uint ifIndex)
        { ErrorText = "请选择出接口（网卡）。"; return null; }

        if (!uint.TryParse(MetricText.Trim(), out uint metric))
        { ErrorText = "跃点数必须是 0~4294967295 的整数。常用 1~5000，越小越优先。"; return null; }

        // 网关须与所选接口同网段的软校验
        if (gw is not null && dest.AddressFamily == AddressFamily.InterNetwork)
        {
            bool reachable = adapter.Ipv4Addresses.Any(ip =>
            {
                var net = IpAddressInfo.ComputeNetwork(ip.Address, Math.Min(ip.PrefixLength, maxLen));
                var gwNet = IpAddressInfo.ComputeNetwork(gw, Math.Min(ip.PrefixLength, maxLen));
                return net.Equals(gwNet);
            });
            if (!reachable)
                ErrorText = $"提示：网关 {gw} 不在网卡“{adapter.Name}”的任何网段内，系统可能拒绝该路由。请确认网关选择与网卡匹配。";
            else
                ErrorText = "";
        }
        else ErrorText = "";

        return new RouteMutation
        {
            Destination = dest,
            PrefixLength = PrefixLength,
            NextHop = IsOnLink ? null : gw,
            InterfaceIndex = ifIndex,
            Metric = metric,
            Persistent = Persistent,
        };
    }
}
