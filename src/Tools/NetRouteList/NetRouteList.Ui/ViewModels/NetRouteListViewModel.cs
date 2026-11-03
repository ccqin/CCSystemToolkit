using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetRouteList.Core.Services;
using Toolkit.Shell;

namespace NetRouteList.Ui.ViewModels;

/// <summary>页面 ViewModel 基类。</summary>
public abstract partial class PageViewModelBase : ObservableObject
{
    public required NetRouteListViewModel Main { get; init; }
    public abstract string Title { get; }
    public abstract string Glyph { get; }
    /// <summary>一句话说明本页用途（侧栏导航悬停显示）。</summary>
    public abstract string NavTip { get; }

    /// <summary>当前系统数据快照（由 Main.RefreshAll 统一刷新）。</summary>
    public SystemSnapshot? Snapshot { get; protected set; }

    public virtual void ApplySnapshot(SystemSnapshot snap) => Snapshot = snap;
}

/// <summary>一次采集的系统快照：网卡 + 路由 + 诊断。所有页面共享同一份数据。</summary>
public sealed class SystemSnapshot
{
    public required List<Core.Models.AdapterInfo> Adapters { get; init; }
    public required List<Core.Models.RouteEntry> RoutesV4 { get; init; }
    public required List<Core.Models.RouteEntry> RoutesV6 { get; init; }
    public required DiagnosticsService.Report Diagnostics { get; init; }
    public DateTime TakenAt { get; } = DateTime.Now;
}

/// <summary>路由管理工具根 ViewModel：工具内导航、全局刷新、权限、日志。</summary>
public partial class NetRouteListViewModel : ObservableObject
{
    /// <summary>宿主（系统管理工具集）能力：权限状态、提权重启。</summary>
    public IToolHost Host { get; }

    public RouteService Routes { get; }
    public AdapterService Adapters { get; }
    public BackupService Backups { get; }
    public DiagnosticsService Diagnostics { get; }

    public DashboardViewModel Dashboard { get; }
    public RoutesViewModel RoutesPage { get; }
    public AdaptersViewModel AdaptersPage { get; }
    public BackupsViewModel BackupsPage { get; }
    public HelpViewModel HelpPage { get; }

    public ReadOnlyObservableCollection<PageViewModelBase> Pages { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    private PageViewModelBase? _currentPage;

    public string PageTitle => CurrentPage?.Title ?? "";

    public bool IsElevated => Host.IsElevated;
    public string PrivilegeText => IsElevated
        ? "管理员 · 可修改路由"
        : "普通权限 · 只读（点此提权）";
    public string PrivilegeColor => IsElevated ? "#107C10" : "#D13438";

    [ObservableProperty]
    private string _statusText = "就绪。数据每页独立刷新，或点击左侧“全部刷新”。";

    /// <summary>操作日志（最新在前）。</summary>
    public ObservableCollection<string> Log { get; } = [];

    public NetRouteListViewModel(IToolHost host, RouteService routes, AdapterService adapters, BackupService backups, DiagnosticsService diagnostics)
    {
        Host = host;
        Routes = routes;
        Adapters = adapters;
        Backups = backups;
        Diagnostics = diagnostics;

        Dashboard = new DashboardViewModel { Main = this };
        RoutesPage = new RoutesViewModel { Main = this };
        AdaptersPage = new AdaptersViewModel { Main = this };
        BackupsPage = new BackupsViewModel { Main = this };
        HelpPage = new HelpViewModel { Main = this };
        var pages = new List<PageViewModelBase> { Dashboard, RoutesPage, AdaptersPage, BackupsPage, HelpPage };
        Pages = new ReadOnlyObservableCollection<PageViewModelBase>(new ObservableCollection<PageViewModelBase>(pages));
        _currentPage = Dashboard;
    }

    public void AddLog(string message)
    {
        Log.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
        while (Log.Count > 300) Log.RemoveAt(Log.Count - 1);
        // 同步归集到宿主主页的统一日志（工具内的本地日志不受影响）
        Host.Log(NetRouteListToolModule.ToolTitle, message);
    }

    [RelayCommand]
    public void RefreshAll()
    {
        try
        {
            var snap = TakeSnapshot();
            foreach (var page in Pages) page.ApplySnapshot(snap);
            StatusText = $"已刷新：{snap.Adapters.Count} 块网卡 · {snap.RoutesV4.Count} 条 IPv4 路由 · {snap.Diagnostics.Issues.Count(i => i.Severity == Core.Models.IssueSeverity.Error)} 个错误 / {snap.Diagnostics.Issues.Count(i => i.Severity == Core.Models.IssueSeverity.Warning)} 个警告";
            AddLog("全部页面已刷新。");
        }
        catch (Exception ex)
        {
            StatusText = "刷新失败：" + ex.Message;
            AddLog("刷新失败：" + ex.Message);
        }
    }

    public SystemSnapshot TakeSnapshot()
    {
        var adapters = Adapters.GetAdapters();
        var v4 = Routes.GetIpv4Routes(adapters);
        var v6 = Routes.GetIpv6Routes(adapters);
        var diag = Diagnostics.Analyze(adapters, v4);
        return new SystemSnapshot
        {
            Adapters = adapters,
            RoutesV4 = v4,
            RoutesV6 = v6,
            Diagnostics = diag,
        };
    }

    /// <summary>以管理员身份重启（提权是进程级操作，交由宿主统一处理）。</summary>
    [RelayCommand]
    private void ElevateRestart()
    {
        if (IsElevated) { StatusText = "当前已是管理员权限。"; return; }
        Host.ElevateRestart();
        // 走到这里说明提权未成功（成功时宿主进程已退出），宿主状态栏已显示原因
        StatusText = Host.StatusText;
        AddLog(Host.StatusText);
    }
}
