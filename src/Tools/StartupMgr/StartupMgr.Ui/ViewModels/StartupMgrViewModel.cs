using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StartupMgr.Core.Models;
using StartupMgr.Core.Services;
using Toolkit.Shell;

namespace StartupMgr.Ui.ViewModels;

/// <summary>启动项行 VM。</summary>
public partial class StartupItemViewModel : ObservableObject
{
    public required StartupEntry Model { get; init; }
    public string Name => Model.Name;
    public string Command => Model.Command;
    public string LocationText => Model.LocationText;
    public string SourceName => Model.SourceName;
    public string? TargetPath => Model.TargetPath;
    public string SourcePath => Model.SourcePath;
    public bool IsResidual => Model.IsResidual;
    public bool IsSystem => Model.IsSystem;
    public bool SupportsToggle => Model.SupportsToggle;
    public bool SupportsDelete => Model.SupportsDelete;

    public string BadgeText => Model.IsResidual ? "⚠ 残留"
        : Model.IsSystem ? "✔ 系统"
        : !Model.IsEnabled ? "⊘ 已禁用" : "";

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private string _statusText = "";

    public void RefreshFromModel()
    {
        IsEnabled = Model.IsEnabled;
        StatusText = Model.StatusText;
        OnPropertyChanged(nameof(BadgeText));
    }
}

/// <summary>服务行 VM。</summary>
public partial class ServiceItemViewModel : ObservableObject
{
    public required ServiceEntry Model { get; init; }
    public string Name => Model.Name;
    public string DisplayName => Model.DisplayName;
    public string ImagePath => Model.ImagePath;
    public string? ServiceDll => Model.ServiceDll;
    public string? Account => Model.Account;
    public string? TargetPath => Model.TargetPath;
    public string SourceName => Model.SourceName;
    public string BadgeText => Model.BadgeText;
    public bool IsResidual => Model.IsResidual;
    public bool IsSystem => Model.IsSystem;

    [ObservableProperty]
    private string _startTypeText = "";

    public void RefreshFromModel() => StartTypeText = Model.StartTypeText;
}

/// <summary>计划任务行 VM。</summary>
public partial class TaskItemViewModel : ObservableObject
{
    public required TaskEntry Model { get; init; }
    public string Path => Model.Path;
    public string Name => Model.Name;
    public string TriggersText => Model.TriggersText;
    public string? Command => Model.Command;
    public string? TargetPath => Model.TargetPath;
    public string LastRunText => Model.LastRunText;
    public string NextRunText => Model.NextRunText;
    public string SourceName => Model.SourceName;
    public string BadgeText => Model.BadgeText;
    public bool IsResidual => Model.IsResidual;

    [ObservableProperty]
    private string _stateText = "";

    public void RefreshFromModel() => StateText = Model.StateText;
}

/// <summary>开机启动项管理主 VM（Tab1 启动项 / Tab2 服务 / Tab3 计划任务）。</summary>
public partial class StartupMgrViewModel : ObservableObject
{
    private readonly IToolHost _host;
    private readonly StartupScanner _scanner = new();
    private readonly StartupCleaner _cleaner = new();
    private readonly ServiceScanner _serviceScanner = new();
    private readonly TaskScanner _taskScanner = new();

    public StartupMgrViewModel(IToolHost host) => _host = host;

    public ObservableCollection<StartupItemViewModel> Entries { get; } = [];
    public ObservableCollection<StartupBackupRecord> Backups { get; } = [];

    public ObservableCollection<ServiceItemViewModel> Services { get; } = [];

    public ObservableCollection<TaskItemViewModel> Tasks { get; } = [];

    /// <summary>计划任务筛选候选。</summary>
    public static IReadOnlyList<string> TaskFilters { get; } = ["登录触发", "仅第三方", "全部"];

    [ObservableProperty]
    private string _taskFilter = "登录触发";

    /// <summary>服务筛选候选。</summary>
    public static IReadOnlyList<string> ServiceFilters { get; } = ["自动启动", "全部含手动", "仅第三方"];

    [ObservableProperty]
    private string _serviceFilter = "自动启动";

    [ObservableProperty]
    private string _searchText = "";

    /// <summary>新增启动项：名称 + 命令。</summary>
    [ObservableProperty]
    private string _newName = "";

    [ObservableProperty]
    private string _newCommand = "";

    [ObservableProperty]
    private string _statusText = "点击「扫描」读取本机开机启动项（只读操作）。";

    [ObservableProperty]
    private bool _isBusy;

    public Avalonia.Controls.Window? Owner { get; set; }

    public int TotalCount => Entries.Count;
    public int ResidualCount => Entries.Count(e => e.IsResidual);
    public int DisabledCount => Entries.Count(e => !e.IsEnabled);
    public int RegistryCount => Entries.Count(e => e.Model.Location is not (StartupLocation.UserFolder or StartupLocation.CommonFolder));

    public int ServiceAutoCount => Services.Count(s => s.Model.StartType == ServiceStartType.Automatic);
    public int ServiceResidualCount => Services.Count(s => s.IsResidual);
    public int ServiceThirdPartyCount => Services.Count(s => !s.IsSystem && s.Model.StartType == ServiceStartType.Automatic);

    public int TaskCount => Tasks.Count;
    public int TaskLogonCount => Tasks.Count(t => t.Model.IsLogonTrigger);
    public int TaskThirdPartyCount => Tasks.Count(t => !t.Model.IsSystem);

    /// <summary>三页残留合计（启动项 + 服务 + 计划任务）。</summary>
    public int ResidualTotal => ResidualCount + ServiceResidualCount + Tasks.Count(t => t.IsResidual);

    /// <summary>计划任务过滤（触发器筛选 + 搜索）。</summary>
    public IEnumerable<TaskItemViewModel> FilteredTasks
    {
        get
        {
            IEnumerable<TaskItemViewModel> q = Tasks;
            q = TaskFilter switch
            {
                "登录触发" => q.Where(t => t.Model.IsLogonTrigger),
                "仅第三方" => q.Where(t => !t.Model.IsSystem),
                _ => q,
            };
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                q = q.Where(t => t.Path.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || t.SourceName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || (t.Command?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));
            }
            return q;
        }
    }

    /// <summary>服务视图过滤（按筛选 + 搜索）。</summary>
    public IEnumerable<ServiceItemViewModel> FilteredServices
    {
        get
        {
            IEnumerable<ServiceItemViewModel> q = Services;
            q = ServiceFilter switch
            {
                "自动启动" => q.Where(s => s.Model.StartType == ServiceStartType.Automatic),
                "仅第三方" => q.Where(s => !s.IsSystem && s.Model.StartType != ServiceStartType.Disabled),
                _ => q,
            };
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                q = q.Where(s => s.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || s.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || s.SourceName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || s.ImagePath.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            }
            return q;
        }
    }

    public IEnumerable<StartupItemViewModel> FilteredEntries
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return Entries;
            return Entries.Where(e => e.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || e.SourceName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || e.Command.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || e.LocationText.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void AddLog(string message)
    {
        _host.Log(StartupMgrToolModule.ToolTitle, message);
        StatusText = message.TrimStart('✔', '✘', ' ', '△');
    }

    [RelayCommand]
    private async Task Scan()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await Task.Delay(120);
            IProgress<string> progress = new Progress<string>(msg => StatusText = msg);
            var models = await Task.Run(() => _scanner.ScanAll(msg => progress.Report(msg)));
            var services = await Task.Run(() => _serviceScanner.ScanAll(msg => progress.Report(msg)));
            var tasks = await Task.Run(() => _taskScanner.ScanAll(msg => progress.Report(msg)));

            Tasks.Clear();
            foreach (var t in tasks) Tasks.Add(new TaskItemViewModel { Model = t });
            foreach (var t in Tasks) t.RefreshFromModel();
            OnPropertyChanged(nameof(FilteredTasks));
            OnPropertyChanged(nameof(TaskCount));
            OnPropertyChanged(nameof(TaskLogonCount));
            OnPropertyChanged(nameof(TaskThirdPartyCount));
            OnPropertyChanged(nameof(ResidualTotal));

            Services.Clear();
            foreach (var sv in services) Services.Add(new ServiceItemViewModel { Model = sv });
            foreach (var sv in Services) sv.RefreshFromModel();
            OnPropertyChanged(nameof(FilteredServices));
            OnPropertyChanged(nameof(ServiceAutoCount));
            OnPropertyChanged(nameof(ServiceResidualCount));
            OnPropertyChanged(nameof(ServiceThirdPartyCount));

            Entries.Clear();
            foreach (var m in models) Entries.Add(new StartupItemViewModel { Model = m });
            foreach (var e in Entries) e.RefreshFromModel();
            RefreshBackups();
            OnPropertyChanged(nameof(FilteredEntries));
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(ResidualCount));
            OnPropertyChanged(nameof(DisabledCount));
            OnPropertyChanged(nameof(RegistryCount));
            AddLog($"✔ 扫描完成：启动项 {TotalCount}（残留 {ResidualCount}）· 服务 {Services.Count}（自动 {ServiceAutoCount}）· 计划任务 {TaskCount}（登录触发 {TaskLogonCount} · 第三方 {TaskThirdPartyCount}）。");
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

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredEntries));
        OnPropertyChanged(nameof(FilteredServices));
        OnPropertyChanged(nameof(FilteredTasks));
        OnPropertyChanged(nameof(FilteredTasks));
        if (!string.IsNullOrWhiteSpace(value))
        {
            StatusText = $"🔍 已过滤：{FilteredEntries.Count()} / {TotalCount} 项匹配";
        }
    }

    [RelayCommand]
    private void ToggleEntry(StartupItemViewModel entry)
    {
        bool wantDisable = entry.Model.IsEnabled;
        var result = _cleaner.Toggle(entry.Model, wantDisable);
        entry.RefreshFromModel();
        AddLog(result.Message);
        OnPropertyChanged(nameof(DisabledCount));
        OnPropertyChanged(nameof(FilteredEntries));
    }

    [RelayCommand]
    private void DeleteEntry(StartupItemViewModel entry)
    {
        var result = _cleaner.Delete(entry.Model);
        if (result.Success)
        {
            Entries.Remove(entry);
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(ResidualCount));
            OnPropertyChanged(nameof(RegistryCount));
            OnPropertyChanged(nameof(FilteredEntries));
            RefreshBackups();
        }
        AddLog(result.Message);
    }

    [RelayCommand]
    private void AddEntry()
    {
        var result = _cleaner.Add(NewName.Trim(), NewCommand.Trim());
        if (result.Success)
        {
            NewName = "";
            NewCommand = "";
            Scan().Wait();
        }
        AddLog(result.Message);
    }

    public void RefreshBackups()
    {
        Backups.Clear();
        foreach (var b in _cleaner.ListBackups().OrderByDescending(b => b.Time).Take(50)) Backups.Add(b);
    }

    [RelayCommand]
    private void RestoreBackup(StartupBackupRecord record)
    {
        AddLog(_cleaner.Restore(record).Message);
        Scan().Wait();
    }

    [RelayCommand]
    private void OpenBackups() => _cleaner.OpenBackupFolder();

    partial void OnServiceFilterChanged(string value) => OnPropertyChanged(nameof(FilteredServices));

    partial void OnTaskFilterChanged(string value) => OnPropertyChanged(nameof(FilteredTasks));

    [RelayCommand]
    private void ToggleTask(TaskItemViewModel item)
    {
        var result = _taskScanner.Change(item.Model.IsEnabled ? StartupMgrTaskAction.Disable : StartupMgrTaskAction.Enable, item.Model.Path);
        if (result.Success)
        {
            item.Model.State = item.Model.IsEnabled ? 3 : 0;
            item.RefreshFromModel();
        }
        AddLog(result.Message);
        OnPropertyChanged(nameof(FilteredTasks));
    }

    [RelayCommand]
    private void DeleteTask(TaskItemViewModel item)
    {
        var result = _taskScanner.Change(StartupMgrTaskAction.Delete, item.Model.Path);
        if (result.Success)
        {
            Tasks.Remove(item);
            OnPropertyChanged(nameof(TaskCount));
            OnPropertyChanged(nameof(FilteredTasks));
        }
        AddLog(result.Message);
    }

    [RelayCommand]
    private void SetServiceManual(ServiceItemViewModel item) => ApplyServiceStart(item, ServiceStartType.Manual);

    [RelayCommand]
    private void SetServiceAuto(ServiceItemViewModel item) => ApplyServiceStart(item, ServiceStartType.Automatic);

    [RelayCommand]
    private void SetServiceDisabled(ServiceItemViewModel item) => ApplyServiceStart(item, ServiceStartType.Disabled);

    private void ApplyServiceStart(ServiceItemViewModel item, ServiceStartType type)
    {
        var result = _serviceScanner.SetStartType(item.Model, type);
        item.RefreshFromModel();
        AddLog(result.Message);
        OnPropertyChanged(nameof(FilteredServices));
        OnPropertyChanged(nameof(ServiceAutoCount));
        OnPropertyChanged(nameof(ServiceThirdPartyCount));
    }

    [RelayCommand]
    private void Elevate() => _host.ElevateRestart();
}
