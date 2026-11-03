using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetRouteList.Core.Models;
using NetRouteList.Core.Services;

namespace NetRouteList.Ui.ViewModels;

public partial class BackupItemViewModel : ObservableObject
{
    public required BackupSnapshot Snapshot { get; init; }
    public string Name => Snapshot.Name;
    public string CreatedAt => Snapshot.CreatedAtLocal.ToString("yyyy-MM-dd HH:mm:ss");
    public string RouteSummary => $"IPv4 {Snapshot.Ipv4RouteCount} 条 · IPv6 {Snapshot.Ipv6RouteCount} 条" +
        (Snapshot.Adapters.Count > 0 ? $" · 网卡 {Snapshot.Adapters.Count} 块" : "");
    public string Note => string.IsNullOrEmpty(Snapshot.Note) ? "" : "备注：" + Snapshot.Note;
    public string Machine => Snapshot.MachineName;
    public string ToolTip => $"创建于 {CreatedAt}（机器 {Snapshot.MachineName}）\n{RouteSummary}\n{Note}\n文件：{Snapshot.FilePath}";
}

public partial class BackupsViewModel : PageViewModelBase
{
    public override string Title => "备份";
    public override string Glyph => "⛁";
    public override string NavTip => "创建/导入/导出路由表备份，与当前路由对比差异，增量合并或完全恢复（恢复前自动安全快照）。";

    public ObservableCollection<BackupItemViewModel> Backups { get; } = [];

    [ObservableProperty] private BackupItemViewModel? _selected;
    [ObservableProperty] private string _summaryText = "尚未加载";

    public bool CanModify => Main.IsElevated;

    public override void ApplySnapshot(SystemSnapshot snap) => Reload();

    public void Reload()
    {
        Backups.Clear();
        foreach (var b in Main.Backups.ListBackups())
            Backups.Add(new BackupItemViewModel { Snapshot = b });
        Selected ??= Backups.FirstOrDefault();
        SummaryText = Backups.Count == 0
            ? $"暂无备份。备份目录：{BackupService.BackupDirectory}"
            : $"共 {Backups.Count} 个备份 · 目录：{BackupService.BackupDirectory}";
    }

    [RelayCommand]
    private void Refresh() => Main.RefreshAll();

    public event Action? CreateRequested;
    [RelayCommand]
    private void CreateBackup() => CreateRequested?.Invoke();

    public event Action<BackupItemViewModel>? RestoreRequested;
    [RelayCommand]
    private void Restore() { if (Selected is { } s) RestoreRequested?.Invoke(s); }

    public event Action<BackupItemViewModel>? DeleteRequested;
    [RelayCommand]
    private void Delete() { if (Selected is { } s) DeleteRequested?.Invoke(s); }

    public event Action<BackupItemViewModel>? DiffRequested;
    [RelayCommand]
    private void Diff() { if (Selected is { } s) DiffRequested?.Invoke(s); }

    public event Action? ExportRequested;
    [RelayCommand]
    private void Export() => ExportRequested?.Invoke();

    public event Action? ImportRequested;
    [RelayCommand]
    private void Import() => ImportRequested?.Invoke();
}

public partial class HelpViewModel : PageViewModelBase
{
    public override string Title => "帮助";
    public override string Glyph => "?";
    public override string NavTip => "路由原理、字段详解、备份/恢复机制、诊断规则与常见问题的完整中文说明。";

    public static string AppIntroText => Core.Docs.HelpDocs.AppIntro;

    public sealed class HelpSection
    {
        public required string Title { get; init; }
        public required string Body { get; init; }
    }

    public ObservableCollection<HelpSection> AllSections { get; } = [];
    public ObservableCollection<HelpSection> VisibleSections { get; } = [];

    public ObservableCollection<KeyValuePair<string, string>> FieldTips { get; } =
        new(Core.Docs.HelpDocs.FieldTips);

    [ObservableProperty] private string _searchText = "";

    public HelpViewModel()
    {
        foreach (var s in Core.Docs.HelpDocs.Sections)
            AllSections.Add(new HelpSection { Title = s.Title, Body = s.Body });
        ApplySearch();
    }

    partial void OnSearchTextChanged(string value) => ApplySearch();

    private void ApplySearch()
    {
        VisibleSections.Clear();
        var q = SearchText?.Trim() ?? "";
        foreach (var s in AllSections)
        {
            if (q.Length == 0 || s.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.Body.Contains(q, StringComparison.OrdinalIgnoreCase))
                VisibleSections.Add(s);
        }
    }
}
