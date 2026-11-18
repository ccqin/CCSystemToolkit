using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using NetTraffic.Core.Models;
using NetTraffic.Ui.Controls;

namespace NetTraffic.Ui.Views;

/// <summary>流量曲线弹窗 VM。</summary>
public partial class TrafficChartWindowViewModel : ObservableObject
{
    public string Title { get; init; } = "";
    public IReadOnlyList<TrafficPoint> Points { get; init; } = [];
    public string StatsText { get; init; } = "";
}

/// <summary>单进程流量曲线弹窗：时间范围内的逐采样点折线（绿下行/橙上行，悬停读值）。</summary>
public partial class TrafficChartWindow : Window
{
    public TrafficChartWindow()
    {
        InitializeComponent();
    }

    /// <summary>弹出曲线窗（points 为 (时间, 下行, 上行) 采样序列）。</summary>
    public static void Show(Window owner, string processName, int pid,
        List<(DateTime Ts, long Down, long Up)> points, string rangeText)
    {
        if (points.Count == 0)
        {
            var empty = new TrafficChartWindow
            {
                DataContext = new TrafficChartWindowViewModel
                {
                    Title = $"{processName}（PID {pid}）· {rangeText}",
                    StatsText = "该时段没有采样数据（流量只在监控运行期间记录）。",
                },
            };
            empty.ShowDialog(owner);
            return;
        }

        var peakDown = points.Max(p => p.Down);
        var peakUp = points.Max(p => p.Up);
        var sumDown = points.Sum(p => p.Down);
        var sumUp = points.Sum(p => p.Up);
        const int period = Core.Services.TrafficHistoryStore.SnapshotSeconds;
        var stats = $"范围 {points[0].Ts:HH:mm:ss} – {points[^1].Ts:HH:mm:ss} · {points.Count} 个采样点（每点 {period} 秒增量）"
            + $" · 合计 ⬇ {ProcessTraffic.FormatBytes(sumDown)} / ⬆ {ProcessTraffic.FormatBytes(sumUp)}"
            + $" · 峰值周期 ⬇ {ProcessTraffic.FormatBytes(peakDown)}（≈{peakDown / (double)period / 1024:F0}KB/s）"
            + $" / ⬆ {ProcessTraffic.FormatBytes(peakUp)}（≈{peakUp / (double)period / 1024:F0}KB/s）";

        var win = new TrafficChartWindow
        {
            DataContext = new TrafficChartWindowViewModel
            {
                Title = $"{processName}（PID {pid}）· {rangeText}",
                Points = points.Select(p => new TrafficPoint(p.Ts, p.Down, p.Up)).ToList(),
                StatsText = stats,
            },
        };
        win.ShowDialog(owner);
    }
}
