using System.Collections.ObjectModel;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MenuClean.Core.Models;
using MenuClean.Core.Services;
using Toolkit.Shell;

namespace MenuClean.Ui.ViewModels;

/// <summary>菜单条目行 VM（场景视图 / 列表视图共用）。</summary>
public partial class EntryItemViewModel : ObservableObject
{
    public required MenuEntry Model { get; init; }
    public string Title => Model.Title;
    public string VerbName => Model.VerbName;

    /// <summary>首字符（模拟菜单头像圆标）。</summary>
    public string FirstChar => string.IsNullOrEmpty(Model.Title) ? "?" : Model.Title.Trim()[..1];
    public string SceneLabel => Model.SceneLabel;
    public string KindText => Model.Kind == MenuEntryKind.Shell ? "命令" : "扩展";
    public string SourceName => Model.SourceName;
    public string? TargetPath => Model.TargetPath;
    public bool IsResidual => Model.IsResidual;
    public bool IsSystem => Model.IsSystem;
    public bool CanDelete => !Model.IsSystem;

    /// <summary>列表首列标记（⚠ 残留 / ✔ 系统 / ⇧ Shift 显示 / ⊘ 已禁用）。</summary>
    public string BadgeText => Model.IsResidual ? "⚠ 残留"
        : Model.IsSystem ? "✔ 系统"
        : !Model.IsEnabled ? "⊘ 已禁用"
        : Model.Status == MenuEntryStatus.ShiftOnly ? "⇧ Shift" : "";

    /// <summary>排序键：系统 verb → 第三方 shell → shellex。</summary>
    public int Order => Model.Kind == MenuEntryKind.Shellex ? 2 : Model.IsSystem ? 0 : 1;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>刷新派生状态。</summary>
    public void RefreshFromModel()
    {
        IsEnabled = Model.IsEnabled;
        StatusText = Model.Status switch
        {
            MenuEntryStatus.DisabledLegacy => "已禁用",
            MenuEntryStatus.Blocked => "已禁用(黑名单)",
            MenuEntryStatus.ShiftOnly => "Shift 显示",
            _ => "启用",
        };
        OnPropertyChanged(nameof(BadgeText));
    }
}

/// <summary>场景节点 VM（场景视图左侧）。</summary>
public partial class SceneItemViewModel : ObservableObject
{
    public required string Label { get; init; }
    public required string Glyph { get; init; }
    public required Func<EntryItemViewModel, bool> Match { get; init; }

    [ObservableProperty]
    private int _count;

    public override string ToString() => Label;
}

/// <summary>来源聚合组 VM（来源视图）。</summary>
public partial class SourceGroupViewModel : ObservableObject
{
    public required string Name { get; init; }
    public List<EntryItemViewModel> Items { get; } = [];

    public int Count => Items.Count;
    public int ResidualCount => Items.Count(i => i.IsResidual);
    public int DisabledCount => Items.Count(i => !i.IsEnabled);
    public bool IsSystem => Name == "Microsoft / 系统";
    public string Summary => $"{Count} 处注入 · {ResidualCount} 残留 · {DisabledCount} 已禁用";
}

/// <summary>清理右键菜单主 VM：扫描 + 三视图（场景 / 来源 / 列表）+ 共享详情面板。</summary>
public partial class MenuCleanViewModel : ObservableObject
{
    private readonly IToolHost _host;
    private readonly ContextMenuScanner _scanner;
    private readonly MenuCleaner _cleaner;
    private readonly MenuBackupService _backup = new();
    private readonly EntryDetailService _detail = new();

    public MenuCleanViewModel(IToolHost host)
    {
        _host = host;
        _cleaner = new MenuCleaner(_backup);
        _scanner = new ContextMenuScanner();
    }

    /// <summary>全部条目（数据源）。</summary>
    public ObservableCollection<EntryItemViewModel> Entries { get; } = [];

    /// <summary>场景列表。</summary>
    public ObservableCollection<SceneItemViewModel> Scenes { get; } = [];

    /// <summary>来源聚合。</summary>
    public ObservableCollection<SourceGroupViewModel> SourceGroups { get; } = [];

    /// <summary>备份文件列表（Flyout）。</summary>
    public ObservableCollection<BackupFile> Backups { get; } = [];

    [ObservableProperty]
    private SceneItemViewModel? _selectedScene;

    /// <summary>当前场景的菜单项（模拟右键菜单渲染：系统→第三方→扩展，分隔线由视图处理两组渲染）。</summary>
    [ObservableProperty]
    private IReadOnlyList<EntryItemViewModel> _menuItems = [];

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _statusText = "点击「扫描」读取本机右键菜单（只读操作，普通权限即可）。";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedEntry))]
    private EntryItemViewModel? _selectedEntry;

    public bool HasSelectedEntry => SelectedEntry is not null;

    /// <summary>详情行（注册表值 + 文件信息，点击条目时实时读取）。</summary>
    public ObservableCollection<DetailRow> DetailRows { get; } = [];

    /// <summary>等效命令（禁用/启用/删除，可复制到管理员终端）。</summary>
    [ObservableProperty]
    private string _detailCommands = "";

    partial void OnSelectedEntryChanged(EntryItemViewModel? value)
    {
        DetailRows.Clear();
        if (value?.Model is not { } m)
        {
            DetailCommands = "";
            return;
        }

        try
        {
            foreach (var row in _detail.GetDetails(m)) DetailRows.Add(row);
        }
        catch (Exception ex)
        {
            DetailRows.Add(new DetailRow("读取失败", ex.Message));
        }

        DetailCommands = string.Join(Environment.NewLine,
            ":: 禁用（可逆）", MenuCleaner.CommandText(m, "disable"),
            ":: 启用", MenuCleaner.CommandText(m, "enable"),
            ":: 删除（先备份）", MenuCleaner.CommandText(m, "delete"));
    }

    // 统计卡
    public int TotalCount => Entries.Count;
    public int ThirdPartyCount => Entries.Count(e => !e.IsSystem);
    public int ResidualCount => Entries.Count(e => e.IsResidual);
    public int DisabledCount => Entries.Count(e => !e.IsEnabled);

    /// <summary>主窗口引用（剪贴板），由视图加载时注入。</summary>
    public Avalonia.Controls.Window? Owner { get; set; }

    [RelayCommand]
    private async Task CopyDetailCommands()
    {
        if (Owner is not { } owner || string.IsNullOrEmpty(DetailCommands)) return;
        var clipboard = Avalonia.Controls.TopLevel.GetTopLevel(owner)?.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetTextAsync(DetailCommands);
        AddLog("✔ 已复制等效命令到剪贴板。");
    }

    public void AddLog(string message)
    {
        _host.Log(MenuCleanToolModule.ToolTitle, message);
        StatusText = message.TrimStart('✔', '✘', ' ', '△');
    }

    [RelayCommand]
    private void Scan()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var models = _scanner.ScanAll((msg, _) => StatusText = msg);

            Entries.Clear();
            foreach (var m in models) Entries.Add(new EntryItemViewModel { Model = m });
            foreach (var e in Entries) e.RefreshFromModel();

            RebuildScenes();
            RebuildSources();
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(ThirdPartyCount));
            OnPropertyChanged(nameof(ResidualCount));
            OnPropertyChanged(nameof(DisabledCount));
            RefreshBackups();

            SelectedScene ??= Scenes.FirstOrDefault();
            UpdateMenuItems();
            AddLog($"✔ 扫描完成：{TotalCount} 项（第三方 {ThirdPartyCount} · 残留 {ResidualCount} · 已禁用 {DisabledCount}）。");
        }
        catch (Exception ex)
        {
            AddLog($"✘ 扫描失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RebuildScenes()
    {
        var previous = SelectedScene?.Label;
        Scenes.Clear();
        foreach (var g in Entries.GroupBy(e => e.Model.SceneLabel))
        {
            Scenes.Add(new SceneItemViewModel
            {
                Label = g.Key,
                Glyph = g.First().Model.Scene switch
                {
                    MenuSceneKind.AnyFile => "📄",
                    MenuSceneKind.Directory => "📁",
                    MenuSceneKind.Background => "🖥",
                    MenuSceneKind.Drive => "💾",
                    _ => "🧩",
                },
                Match = e => e.Model.SceneLabel == g.Key,
                Count = g.Count(),
            });
        }
        SelectedScene = Scenes.FirstOrDefault(s => s.Label == previous) ?? Scenes.FirstOrDefault();
    }

    private void RebuildSources()
    {
        SourceGroups.Clear();
        foreach (var g in Entries.Where(MatchesSearch).GroupBy(e => e.SourceName).OrderBy(g => g.Key))
        {
            var group = new SourceGroupViewModel { Name = g.Key };
            group.Items.AddRange(g);
            SourceGroups.Add(group);
        }
    }

    partial void OnSelectedSceneChanged(SceneItemViewModel? value) => UpdateMenuItems();

    private void UpdateMenuItems()
    {
        if (SelectedScene is not { } scene) { MenuItems = []; return; }
        MenuItems = Entries.Where(scene.Match)
            .Where(MatchesSearch)
            .OrderBy(e => e.Order)
            .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        scene.Count = MenuItems.Count;
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredEntries));   // 列表页刷新
        UpdateMenuItems();                            // 场景视图菜单过滤
        RebuildSources();                             // 来源视图分组过滤
        if (!string.IsNullOrWhiteSpace(value))
        {
            StatusText = $"🔍 已过滤：{Entries.Count(MatchesSearch)} / {Entries.Count} 项匹配“{value}”（三个视图同时生效）";
        }
    }

    /// <summary>单个条目是否匹配搜索（名称/来源/场景/注册表键）。</summary>
    private bool MatchesSearch(EntryItemViewModel e)
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        return e.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
               || e.SourceName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
               || e.SceneLabel.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
               || e.Model.RegistryPath.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
               || (e.Model.TargetPath?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    /// <summary>列表视图过滤（搜索 + 全量）。</summary>
    public IEnumerable<EntryItemViewModel> FilteredEntries =>
        string.IsNullOrWhiteSpace(SearchText)
            ? Entries
            : Entries.Where(MatchesSearch);

    [RelayCommand]
    private void ToggleEntry(EntryItemViewModel entry)
    {
        // 以模型当前状态为方向（ToggleSwitch 视觉由 IsEnabled 单向驱动，命令时序无关）
        bool wantDisable = entry.Model.IsEnabled;
        var result = wantDisable ? _cleaner.Disable(entry.Model) : _cleaner.Enable(entry.Model);
        if (result.Success)
        {
            entry.Model.Status = wantDisable
                ? (entry.Model.Kind == MenuEntryKind.Shell ? MenuEntryStatus.DisabledLegacy : MenuEntryStatus.Blocked)
                : MenuEntryStatus.Enabled;
            entry.RefreshFromModel();
        }
        AddLog(result.Message);
        UpdateMenuItems();
        RebuildSources();
        OnPropertyChanged(nameof(DisabledCount));
    }

    [RelayCommand]
    private void DeleteEntry(EntryItemViewModel entry)
    {
        var result = _cleaner.Delete(entry.Model);
        if (result.Success)
        {
            Entries.Remove(entry);
            RebuildScenes();
            RebuildSources();
            UpdateMenuItems();
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(ThirdPartyCount));
            OnPropertyChanged(nameof(ResidualCount));
            RefreshBackups();
        }
        AddLog(result.Message);
    }

    /// <summary>禁用/启用后就地翻转状态（LegacyDisable/黑名单的写入结果是确定的，无需重扫）。</summary>
    private void RescanSingle(EntryItemViewModel entry)
    {
        entry.Model.Status = entry.Model.IsEnabled switch
        {
            // 原来启用 → 现在应禁用；原来禁用 → 现在应启用（Blocked/Legacy 统一映射）
            true => entry.Model.Kind == MenuEntryKind.Shell
                ? MenuEntryStatus.DisabledLegacy
                : MenuEntryStatus.Blocked,
            _ => MenuEntryStatus.Enabled,
        };
        entry.RefreshFromModel();
    }

    /// <summary>一键清理全部残留项（删除前逐项自动备份）。</summary>
    [RelayCommand]
    private void CleanResidual()
    {
        var targets = Entries.Where(e => e.IsResidual).ToList();
        if (targets.Count == 0)
        {
            AddLog("△ 没有发现残留项（指向已不存在文件的菜单入口）。");
            return;
        }

        int ok = 0;
        foreach (var t in targets)
        {
            if (_cleaner.Delete(t.Model).Success) ok++;
        }
        Scan();   // 全量重扫刷新
        AddLog($"✔ 残留清理完成：{ok}/{targets.Count} 项已删除（删除前均已自动 .reg 备份）。");
    }

    [RelayCommand]
    private void ToggleSource(SourceGroupViewModel group)
    {
        int changed = 0;
        bool disableAny = group.Items.Any(i => i.IsEnabled);
        foreach (var item in group.Items)
        {
            var wantDisable = disableAny;
            if (wantDisable == !item.IsEnabled) continue;
            var r = wantDisable ? _cleaner.Disable(item.Model) : _cleaner.Enable(item.Model);
            if (r.Success) changed++;
        }
        AddLog(changed > 0
            ? $"✔ 「{group.Name}」已{(disableAny ? "全部禁用" : "全部启用")}（{changed} 处）。"
            : "△ 没有需要变更的条目。");
        Scan();
    }

    // ----- 备份 -----

    public void RefreshBackups()
    {
        Backups.Clear();
        foreach (var b in _backup.List()) Backups.Add(b);
    }

    [RelayCommand]
    private void RestoreBackup(BackupFile file)
    {
        AddLog(_backup.Restore(file.FullPath).Message);
        Scan();
    }

    [RelayCommand]
    private void OpenBackups() => _backup.OpenFolder();

    /// <summary>重启资源管理器（shellex 扩展的禁用需新进程生效；shell 命令项即时生效）。</summary>
    [RelayCommand]
    private void RestartExplorer()
    {
        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("explorer")) p.Kill();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            AddLog("✔ 资源管理器已重启，shellex 扩展的禁用/启用立即生效。");
        }
        catch (Exception ex)
        {
            AddLog($"✘ 重启资源管理器失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private void Elevate() => _host.ElevateRestart();
}
