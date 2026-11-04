using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShellExt.Core.Models;
using ShellExt.Core.Services;
using Toolkit.Shell;

namespace ShellExt.Ui.ViewModels;

/// <summary>COM 扩展条目行 VM。</summary>
public partial class ExtItemViewModel : ObservableObject
{
    public required ShellExtEntry Model { get; init; }
    public string Description => Model.Description;
    public string Clsid => Model.Clsid;
    public string KindText => Model.KindText;
    public string SourceName => Model.SourceName;
    public string? Dll => Model.Dll;
    public int Occurrences => Model.Occurrences;
    public bool IsResidual => Model.IsResidual;
    public bool IsSystem => Model.IsSystem;

    /// <summary>疑似菜单文字（DLL 字符串资源扫描）。</summary>
    public string SuspectedTexts => Model.SuspectedTextsText;

    public string BadgeText => Model.IsResidual ? "⚠ 残留" : Model.IsSystem ? "✔ 系统" : !Model.IsEnabled ? "⊘ 已禁用" : "";

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

/// <summary>COM 扩展管理器主 VM。</summary>
public partial class ShellExtViewModel : ObservableObject
{
    private readonly IToolHost _host;
    private readonly ShellExtScanner _scanner = new();
    private readonly ShellExtCleaner _cleaner = new();

    public ShellExtViewModel(IToolHost host) => _host = host;

    public ObservableCollection<ExtItemViewModel> Entries { get; } = [];

    /// <summary>类型筛选候选。</summary>
    public static IReadOnlyList<string> KindFilters { get; } = ["全部", "右键菜单", "图标叠加", "预览", "缩略图", "属性页", "其他"];

    [ObservableProperty]
    private string _kindFilter = "全部";

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _statusText = "点击「扫描」读取本机 COM Shell 扩展（只读操作）。";

    [ObservableProperty]
    private bool _isBusy;

    public Avalonia.Controls.Window? Owner { get; set; }

    public int TotalCount => Entries.Count;
    public int ResidualCount => Entries.Count(e => e.IsResidual);
    public int DisabledCount => Entries.Count(e => !e.IsEnabled);
    public int OverlayCount => Entries.Count(e => e.KindText == "图标叠加");

    public IEnumerable<ExtItemViewModel> FilteredEntries
    {
        get
        {
            IEnumerable<ExtItemViewModel> q = Entries;
            if (KindFilter != "全部") q = q.Where(e => e.KindText == KindFilter);
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                q = q.Where(e => e.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || e.Clsid.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || e.SourceName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || e.SuspectedTexts.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || (e.Dll?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));
            }
            return q;
        }
    }

    public void AddLog(string message)
    {
        _host.Log(ShellExtToolModule.ToolTitle, message);
        StatusText = message.TrimStart('✔', '✘', ' ', '△');
    }

    [RelayCommand]
    private async Task Scan()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await Task.Delay(120);   // 让遮罩先渲染一帧
            IProgress<string> progress = new Progress<string>(msg => StatusText = msg);   // Report 自动回 UI 线程
            var models = await Task.Run(() => _scanner.ScanAll(msg => progress.Report(msg)));

            Entries.Clear();
            foreach (var m in models) Entries.Add(new ExtItemViewModel { Model = m });
            foreach (var e in Entries) e.RefreshFromModel();
            OnPropertyChanged(nameof(FilteredEntries));
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(ResidualCount));
            OnPropertyChanged(nameof(DisabledCount));
            OnPropertyChanged(nameof(OverlayCount));
            AddLog($"✔ 扫描完成：{TotalCount} 个扩展（残留 {ResidualCount} · 已禁用 {DisabledCount} · 图标叠加 {OverlayCount}）。");
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

    partial void OnKindFilterChanged(string value) => OnPropertyChanged(nameof(FilteredEntries));

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredEntries));
        if (!string.IsNullOrWhiteSpace(value))
        {
            StatusText = $"🔍 已过滤：{FilteredEntries.Count()} / {TotalCount} 个匹配（含描述 / CLSID / DLL 路径）";
        }
    }

    [RelayCommand]
    private void ToggleEntry(ExtItemViewModel entry)
    {
        bool wantBlock = entry.Model.IsEnabled;
        var result = _cleaner.SetBlocked(entry.Model, wantBlock);
        entry.RefreshFromModel();
        AddLog(result.Message);
        OnPropertyChanged(nameof(DisabledCount));
        OnPropertyChanged(nameof(FilteredEntries));
    }

    /// <summary>重启资源管理器（禁用/启用扩展后需新进程才生效；桌面会闪一下属正常）。</summary>
    [RelayCommand]
    private void RestartExplorer()
    {
        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("explorer")) p.Kill();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            AddLog("✔ 资源管理器已重启，扩展禁用/启用立即生效。");
        }
        catch (Exception ex)
        {
            AddLog($"✘ 重启资源管理器失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private void Elevate() => _host.ElevateRestart();
}
