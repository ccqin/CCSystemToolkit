using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetRouteList.Core.Models;

namespace NetRouteList.Ui.ViewModels;

/// <summary>诊断问题的显示包装（供界面绑定）。</summary>
public partial class DiagnosticIssueViewModel : ObservableObject
{
    public required DiagnosticIssue Issue { get; init; }

    public IssueSeverity Severity => Issue.Severity;
    public string SeverityText => Severity switch
    {
        IssueSeverity.Error => "错误",
        IssueSeverity.Warning => "警告",
        _ => "信息",
    };
    public string SeverityColor => Severity switch
    {
        IssueSeverity.Error => "#D13438",
        IssueSeverity.Warning => "#C19C00",
        _ => "#0078D4",
    };
    public string Title => Issue.Title;
    public string Explanation => Issue.Explanation;
    public string Suggestion => Issue.Suggestion;

    /// <summary>直接引用 Issue 的明细（构造时 Issue 尚未赋值，不能在此刻拷贝集合）。</summary>
    public IReadOnlyList<string> Affected => Issue.Affected;
    public bool HasAffected => Issue.Affected.Count > 0;
    public bool HasRepair => Issue.Repair is not null;
}

/// <summary>总览页：统计卡片 + 诊断列表 + 一键修复。</summary>
public partial class DashboardViewModel : PageViewModelBase
{
    public override string Title => "总览";
    public override string Glyph => "⌂";
    public override string NavTip => "统计卡片与自动诊断：多默认路由、缺失 on-link 路由、网段冲突等问题，附解释与一键修复。";

    [ObservableProperty] private int _adapterCount;
    [ObservableProperty] private int _routeCountV4;
    [ObservableProperty] private int _defaultRouteCount;
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private string _takenAtText = "尚未刷新";
    [ObservableProperty] private bool _hasData;

    public ObservableCollection<DiagnosticIssueViewModel> Issues { get; } = [];

    public override void ApplySnapshot(SystemSnapshot snap)
    {
        base.ApplySnapshot(snap);
        var d = snap.Diagnostics;
        AdapterCount = d.AdapterCount;
        RouteCountV4 = d.RouteCountV4;
        DefaultRouteCount = d.DefaultRouteCount;
        ErrorCount = d.ErrorCount;
        WarningCount = d.WarningCount;
        TakenAtText = $"数据采集于 {snap.TakenAt:HH:mm:ss}";
        HasData = true;

        Issues.Clear();
        foreach (var issue in d.Issues)
            Issues.Add(new DiagnosticIssueViewModel { Issue = issue });
    }

    [RelayCommand]
    private void Refresh() => Main.RefreshAll();

    /// <summary>执行一键修复（在界面层弹出确认对话框后调用）。</summary>
    [RelayCommand]
    private void Repair(object param)
    {
        if (param is not DiagnosticIssueViewModel vm || vm.Issue.Repair is not { } repair) return;
        // 确认与结果展示由 View 层的对话框服务处理；这里只暴露入口。
        RepairRequest?.Invoke(vm);
    }

    /// <summary>View 订阅此事件以弹出确认框并执行修复。</summary>
    public event Action<DiagnosticIssueViewModel>? RepairRequest;
}
