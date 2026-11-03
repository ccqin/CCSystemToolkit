using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using Toolkit.Shell;

namespace Toolkit.App.ViewModels;

/// <summary>日志工具页 ViewModel：在宿主统一日志集合上提供筛选、统计与清空。</summary>
public partial class LogsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private int _okCount;
    private int _errCount;

    /// <summary>级别筛选项。</summary>
    public static IReadOnlyList<string> LevelFilters { get; } = ["全部", "信息", "成功", "错误"];

    [ObservableProperty]
    private string _levelFilter = "全部";

    [ObservableProperty]
    private string _sourceFilter = "全部";

    /// <summary>来源筛选候选（"全部" + 日志中出现过的来源，随日志动态维护）。</summary>
    public ObservableCollection<string> Sources { get; } = ["全部"];

    /// <summary>过滤视图（列表 ItemsSource；筛选条件变化时 Refresh）。</summary>
    public DataGridCollectionView View { get; }

    [ObservableProperty]
    private string _summaryText = "共 0 条";

    /// <summary>清空日志：直接转发宿主的清空命令（状态栏 Flyout 与本页行为一致）。</summary>
    public ICommand ClearLogsCommand => _main.ClearLogsCommand;

    public LogsViewModel(MainViewModel main)
    {
        _main = main;
        View = new DataGridCollectionView(main.Logs) { Filter = Filter };
        main.Logs.CollectionChanged += OnLogsChanged;
        RebuildSources();
        UpdateSummary();
    }

    partial void OnLevelFilterChanged(string value) => View.Refresh();

    partial void OnSourceFilterChanged(string value) => View.Refresh();

    private bool Filter(object obj)
    {
        if (obj is not ToolLogEntry e) return false;
        if (LevelFilter != "全部" && LevelName(e.Level) != LevelFilter) return false;
        if (SourceFilter != "全部" && e.Source != SourceFilter) return false;
        return true;
    }

    private static string LevelName(ToolLogLevel level) => level switch
    {
        ToolLogLevel.Success => "成功",
        ToolLogLevel.Error => "错误",
        _ => "信息",
    };

    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems is { Count: > 0 } && e.NewItems[0] is ToolLogEntry added)
                    Bump(added, +1);
                break;
            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems is { Count: > 0 } && e.OldItems[0] is ToolLogEntry removed)
                    Bump(removed, -1);
                break;
            case NotifyCollectionChangedAction.Reset:
                _okCount = _errCount = 0;
                foreach (var entry in _main.Logs) Bump(entry, +1);
                break;
        }
        RebuildSources();
        UpdateSummary();
    }

    private void Bump(ToolLogEntry entry, int delta)
    {
        if (entry.Level == ToolLogLevel.Success) _okCount += delta;
        else if (entry.Level == ToolLogLevel.Error) _errCount += delta;
    }

    private void RebuildSources()
    {
        var current = SourceFilter;
        Sources.Clear();
        Sources.Add("全部");
        foreach (var s in _main.Logs.Select(x => x.Source).Distinct().OrderBy(x => x))
            Sources.Add(s);
        SourceFilter = Sources.Contains(current) ? current : "全部";
    }

    private void UpdateSummary() =>
        SummaryText = $"共 {_main.Logs.Count} 条 · ✔ {_okCount} · ✘ {_errCount}";
}
