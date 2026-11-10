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
}
