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

/// <summary>开机启动项管理主 VM。</summary>
public partial class StartupMgrViewModel : ObservableObject
{
    private readonly IToolHost _host;
    private readonly StartupScanner _scanner = new();
    private readonly StartupCleaner _cleaner = new();

    public StartupMgrViewModel(IToolHost host) => _host = host;

    public ObservableCollection<StartupItemViewModel> Entries { get; } = [];
    public ObservableCollection<StartupBackupRecord> Backups { get; } = [];

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

            Entries.Clear();
            foreach (var m in models) Entries.Add(new StartupItemViewModel { Model = m });
            foreach (var e in Entries) e.RefreshFromModel();
            RefreshBackups();
            OnPropertyChanged(nameof(FilteredEntries));
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(ResidualCount));
            OnPropertyChanged(nameof(DisabledCount));
            OnPropertyChanged(nameof(RegistryCount));
            AddLog($"✔ 扫描完成：{TotalCount} 项（注册表 {RegistryCount} · 残留 {ResidualCount} · 已禁用 {DisabledCount}）。");
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

    [RelayCommand]
    private void Elevate() => _host.ElevateRestart();
}
