using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.Input;
using NetTraffic.Core.Models;
using System.Diagnostics;
using NetTraffic.Core.Services;
using Toolkit.Shell;

namespace NetTraffic.Ui.ViewModels;

/// <summary>流量监控主 VM：ETW 实时按进程速率 + 1 秒快照刷新。</summary>
public partial class NetTrafficViewModel : ObservableObject
{
    private readonly IToolHost _host;
    private readonly EtwTrafficMonitor _monitor = new();
    private readonly Dictionary<int, ProcessTraffic> _view = [];
    private DispatcherTimer? _timer;
    private int _noTrafficStreak;

    public NetTrafficViewModel(IToolHost host)
    {
        _host = host;
    }

    public ObservableCollection<ProcessTraffic> Rows { get; } = [];

    [ObservableProperty]
    private bool _running;

    /// <summary>右键菜单作用的选中行。</summary>
    [ObservableProperty]
    private ProcessTraffic? _selectedRow;

    public Avalonia.Controls.Window? Owner { get; set; }

    [ObservableProperty]
    private string _totalDownText = "—";

    [ObservableProperty]
    private string _totalUpText = "—";

    [ObservableProperty]
    private string _topText = "";

    [ObservableProperty]
    private string _statusText = "点击「开始监控」实时查看每个进程的下载/上传速率（需要管理员）。";

    public void AddLog(string message) => _host.Log(NetTrafficToolModule.ToolTitle, message);

    [RelayCommand]
    private void ToggleMonitor()
    {
        if (Running)
        {
            _timer?.Stop();
            _monitor.Stop();
            Running = false;
            StatusText = "监控已停止。";
            AddLog("监控已停止。");
            return;
        }

        if (!_monitor.Start(out var error))
        {
            StatusText = error;
            AddLog("✘ " + error);
            return;
        }

        _view.Clear();
        Rows.Clear();
        _noTrafficStreak = 0;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Snapshot();
        _timer.Start();
        Running = true;
        StatusText = "监控中…列表按下载速率排序，实时刷新（每秒）。";
        AddLog("✔ 实时监控已启动（内核 ETW：TCP+UDP 事件按进程归因）。");
    }

    private void Snapshot()
    {
        var list = _monitor.Snapshot(_view, out double totalDown, out double totalUp);

        // 同步视图字典（新增的进程登记）
        foreach (var item in list)
            if (!_view.ContainsKey(item.Pid))
                _view[item.Pid] = item;

        // 连接数（每 2 秒取一次，注册表表开销小但没必要每秒）
        var conns = _snapshotCount++ % 2 == 0 ? ConnectionCounter.CountByPid() : null;
        if (conns is not null)
        {
            foreach (var item in list)
                item.Connections = conns.TryGetValue(item.Pid, out int n) ? n : 0;
        }

        Rows.Clear();
        foreach (var item in list.Take(200)) Rows.Add(item);

        TotalDownText = ProcessTraffic.FormatSpeed(totalDown);
        TotalUpText = ProcessTraffic.FormatSpeed(totalUp);
        TopText = list.Count > 0 ? $"⬇ 最活跃：{list[0].Name}（{list[0].DownText}）" : "";

        if (list.Count == 0 && ++_noTrafficStreak > 10)
        {
            _noTrafficStreak = 0;
            StatusText = "尚无流量事件。若持续为空，请确认以管理员身份运行，并浏览网页/下载文件产生流量后观察。";
        }
    }

    private int _snapshotCount;

    [RelayCommand]
    private void ResetTotals()
    {
        _monitor.ResetTotals();
        foreach (var row in Rows) { row.DownTotal = 0; row.UpTotal = 0; }
        AddLog("已清零会话累计流量。");
    }

    // ---------- 右键菜单 ----------

    [RelayCommand]
    private async Task ShowDetail()
    {
        if (SelectedRow is not { } row) return;
        var detail = await Task.Run(() => ProcessDetailService.GetDetail(row.Pid, row.Name));
        var conns = await Task.Run(() => ProcessDetailService.GetConnections(row.Pid));
        if (Owner is not { } owner) return;
        var win = new Views.TrafficDetailWindow
        {
            DataContext = new TrafficDetailViewModel(detail, row.DownTotalText, row.UpTotalText, conns),
        };
        await win.ShowDialog(owner);
    }

    [RelayCommand]
    private void OpenFileLocation()
    {
        if (SelectedRow is not { } row) return;
        try
        {
            var detail = ProcessDetailService.GetDetail(row.Pid, row.Name);
            if (detail.Path is { } exe)
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{exe}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex) { AddLog("✘ 打开文件位置失败：" + ex.Message); }
    }

    [RelayCommand]
    private void WebSearch()
    {
        if (SelectedRow is not { } row) return;
        try
        {
            var url = $"https://www.bing.com/search?q={Uri.EscapeDataString(row.Name + " 进程 是什么")}";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    [RelayCommand]
    private async Task CopyName()
    {
        if (SelectedRow is not { } row || Owner is not { } owner) return;
        if (Avalonia.Controls.TopLevel.GetTopLevel(owner)?.Clipboard is { } clip)
        {
            await clip.SetTextAsync(row.Name);
            StatusText = $"已复制进程名 {row.Name}";
        }
    }

    [RelayCommand]
    private void KillProcess()
    {
        if (SelectedRow is not { } row) return;
        try
        {
            using var p = Process.GetProcessById(row.Pid);
            p.Kill(entireProcessTree: true);
            AddLog($"✔ 已结束进程 {row.Name}（PID {row.Pid}）。");
            StatusText = $"已结束 {row.Name}（PID {row.Pid}）";
        }
        catch (Exception ex)
        {
            AddLog("✘ 结束失败：" + ex.Message);
            StatusText = "结束失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void Elevate() => _host.ElevateRestart();

    // ===== 历史分析（SQLite net_traffic 表，监控运行时每 10 秒落库） =====

    public static readonly IReadOnlyList<string> HistoryRanges =
        ["最近 15 分钟", "最近 1 小时", "最近 6 小时", "今天", "最近 3 天"];

    public ObservableCollection<HistoryRowViewModel> HistoryRows { get; } = [];

    /// <summary>历史表选中行（双击/右键查看曲线用）。</summary>
    [ObservableProperty] private HistoryRowViewModel? _selectedHistoryRow;

    [ObservableProperty] private int _historyRangeIndex = 1;
    [ObservableProperty] private string _historyStats = "尚未查询";

    private DateTime RangeFrom() => HistoryRangeIndex switch
    {
        0 => DateTime.Now.AddMinutes(-15),
        1 => DateTime.Now.AddHours(-1),
        2 => DateTime.Now.AddHours(-6),
        3 => DateTime.Today,
        _ => DateTime.Now.AddDays(-3),
    };

    private string RangeText => HistoryRanges[HistoryRangeIndex];

    [RelayCommand]
    private void LoadHistory()
    {
        var from = RangeFrom();
        var list = TrafficHistoryStore.QuerySummary(from, DateTime.Now);
        HistoryRows.Clear();
        foreach (var item in list.Take(500))
            HistoryRows.Add(new HistoryRowViewModel { Model = item });
        var down = list.Sum(x => x.DownBytes);
        var up = list.Sum(x => x.UpBytes);
        HistoryStats = list.Count == 0
            ? $"该时段没有历史数据——流量只在监控运行期间记录（每 {TrafficHistoryStore.SnapshotSeconds} 秒采样，保留 14 天）。"
            : $"共 {list.Count} 个进程 · ⬇ {ProcessTraffic.FormatBytes(down)} · ⬆ {ProcessTraffic.FormatBytes(up)} · 库中累计 {TrafficHistoryStore.RowCount()} 行";
    }

    [RelayCommand]
    private async Task CopyHistory()
    {
        if (HistoryRows.Count == 0) return;
        if (Owner is not { } owner) return;
        if (Avalonia.Controls.TopLevel.GetTopLevel(owner)?.Clipboard is not { } clip) return;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("进程	PID	下行	上行	合计	活跃周期	首次出现	最后出现");
        foreach (var r in HistoryRows)
            sb.AppendLine($"{r.Name}	{r.Pid}	{r.DownText}	{r.UpText}	{r.TotalText}	{r.PeriodsText}	{r.FirstSeen:HH:mm:ss}	{r.LastSeen:HH:mm:ss}");
        await clip.SetTextAsync(sb.ToString());
        _host.Log(NetTrafficToolModule.ToolTitle, $"✔ 已复制 {HistoryRows.Count} 行流量历史");
    }

    /// <summary>右键某行查看该进程的逐采样点流量折线（弹窗，绿下行/橙上行，悬停读值）。</summary>
    [RelayCommand]
    private void ShowTrafficChart()
    {
        if (SelectedHistoryRow is not { } row || Owner is not { } owner) return;
        var points = TrafficHistoryStore.QuerySeries(row.Pid, RangeFrom(), DateTime.Now);
        Views.TrafficChartWindow.Show(owner, row.Name, row.Pid, points, RangeText);
    }

    // ===== 代理分析（Clash/FlClash 内核 external-controller：谁在走代理） =====

    public ObservableCollection<ClashProcessAgg> ProxyByProcess { get; } = [];
    public ObservableCollection<ClashConnection> ProxyConnections { get; } = [];

    [ObservableProperty] private string _proxyBaseUri = "http://127.0.0.1:9090";
    [ObservableProperty] private string _proxySecret = "";
    [ObservableProperty] private bool _proxyAuto;
    [ObservableProperty] private string _proxyStats = "填好地址（FlClash 默认 127.0.0.1:9090）后点「查询」";

    private DispatcherTimer? _proxyTimer;

    partial void OnProxyAutoChanged(bool value)
    {
        if (value)
        {
            _proxyTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _proxyTimer.Tick += (_, _) => _ = LoadProxyCoreAsync();
            _proxyTimer.Start();
            _ = LoadProxyCoreAsync();
        }
        else
        {
            _proxyTimer?.Stop();
        }
    }

    [RelayCommand]
    private Task LoadProxyAsync() => LoadProxyCoreAsync();

    private async Task LoadProxyCoreAsync()
    {
        try
        {
            var result = await ClashApiService.GetConnectionsAsync(ProxyBaseUri, ProxySecret);
            ProxyByProcess.Clear();
            foreach (var a in result.ByProcess) ProxyByProcess.Add(a);
            ProxyConnections.Clear();
            foreach (var c in result.Connections.Take(500)) ProxyConnections.Add(c);
            ProxyStats = $"活跃连接 {result.Connections.Count} 条 · 按进程 {result.ByProcess.Count} 个"
                + $" · 活跃流量 ⬇ {ProcessTraffic.FormatBytes(result.Connections.Sum(c => c.Download))}"
                + $" / ⬆ {ProcessTraffic.FormatBytes(result.Connections.Sum(c => c.Upload))}"
                + $" · 内核累计 ⬇ {ProcessTraffic.FormatBytes(result.TotalDownload)} / ⬆ {ProcessTraffic.FormatBytes(result.TotalUpload)}";
        }
        catch (Exception ex)
        {
            ProxyStats = "查询失败：" + ex.Message + "（核对 FlClash「外部控制」地址与密钥；仅本机代理内核可查）";
        }
    }

}
/// <summary>流量历史汇总行 VM（按进程聚合一个时间范围）。</summary>
public partial class HistoryRowViewModel : ObservableObject
{
    public required TrafficHistorySummary Model { get; init; }

    public string Name => Model.Name;
    public int Pid => Model.Pid;
    public string DownText => "⬇ " + ProcessTraffic.FormatBytes(Model.DownBytes);
    public string UpText => "⬆ " + ProcessTraffic.FormatBytes(Model.UpBytes);
    public string TotalText => ProcessTraffic.FormatBytes(Model.TotalBytes);
    public string PeriodsText => $"{Model.ActivePeriods} 个周期";
    public DateTime FirstSeen => Model.FirstSeen;
    public DateTime LastSeen => Model.LastSeen;

}
