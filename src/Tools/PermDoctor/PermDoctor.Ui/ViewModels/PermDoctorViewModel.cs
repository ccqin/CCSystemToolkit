using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PermDoctor.Core.Models;
using PermDoctor.Core.Services;
using Toolkit.Shell;

namespace PermDoctor.Ui.ViewModels;

/// <summary>权限医生工具页 ViewModel：路径输入 → 诊断报告（ACL/有效权限/写入实测）→ 一键修复。</summary>
public partial class PermDoctorViewModel : ObservableObject
{
    private readonly IToolHost _host;
    private readonly DiagnoseService _diagnose;
    private readonly FixService _fix = new();
    private DiagnosisReport? _report;

    public PermDoctorViewModel(IToolHost host, DiagnoseService diagnose)
    {
        _host = host;
        _diagnose = diagnose;
    }

    /// <summary>主窗口引用（浏览文件夹对话框 Owner），由视图加载时注入。</summary>
    public Window? Owner { get; set; }

    [ObservableProperty]
    private string _path = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReport))]
    private DiagnosisReport? _reportPublic;

    public bool HasReport => ReportPublic is not null;

    /// <summary>ACL 明细（DataGrid 绑定）。</summary>
    public ObservableCollection<AclEntryView> Entries { get; } = [];

    /// <summary>等效命令（mono 文本块）。</summary>
    [ObservableProperty]
    private string _commands = "";

    /// <summary>最近一次修复/诊断的操作结果（状态行）。</summary>
    [ObservableProperty]
    private string _lastMessage = "输入或选择要诊断的文件夹。";

    public void AddLog(string message)
    {
        _host.Log(PermDoctorToolModule.ToolTitle, message);
        LastMessage = message.TrimStart('✔', '✘', ' ', '△');
    }

    [RelayCommand]
    private async Task Browse()
    {
        if (Owner is not { } owner) return;
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "选择要诊断的文件夹",
            AllowMultiple = false,
        });
        if (folders.Count > 0) Path = folders[0].Path.LocalPath;
    }

    [RelayCommand]
    private void Diagnose()
    {
        if (string.IsNullOrWhiteSpace(Path))
        {
            AddLog("✘ 请先输入文件夹路径。");
            return;
        }

        try
        {
            _report = _diagnose.Diagnose(Path);
            ReportPublic = _report;
            Entries.Clear();
            foreach (var e in _report.Entries) Entries.Add(e);
            Commands = string.Join(Environment.NewLine, _report.FixCommands);

            AddLog(_report.WriteTest switch
            {
                WriteTestResult.Ok => $"✔ 诊断完成：{Path} 权限正常（有效权限：{_report.EffectiveRightsText}）。",
                WriteTestResult.CreateDenied => $"✘ 诊断完成：创建子目录被拒（错误 5 场景）；有效权限：{_report.EffectiveRightsText}。",
                _ => $"△ 诊断完成：{_report.WriteTest}（{_report.WriteTestError}）；有效权限：{_report.EffectiveRightsText}。",
            });
        }
        catch (Exception ex)
        {
            AddLog($"✘ 诊断失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private void FixGrant()
    {
        if (EnsureReported() is not { } r) return;
        var result = _fix.GrantModify(r.Path);
        AddLog(result.Message);
        if (result.Success) Diagnose();
    }

    [RelayCommand]
    private void FixReset()
    {
        if (EnsureReported() is not { } r) return;
        var result = _fix.EnableInheritance(r.Path, recursive: true);
        AddLog(result.Message);
        if (result.Success) Diagnose();
    }

    [RelayCommand]
    private void FixOwner()
    {
        if (EnsureReported() is not { } r) return;
        var result = _fix.TakeOwnership(r.Path);
        AddLog(result.Message);
    }

    /// <summary>删除一条授权（仅孤儿 SID 行提供按钮；系统关键授权在服务层被阻止）。</summary>
    [RelayCommand]
    private void RemoveEntry(AclEntryView entry)
    {
        if (_report is not { } r) return;
        var result = _fix.RemoveRule(r.Path, entry);
        AddLog(result.Message);
        if (result.Success) Diagnose();
    }

    [RelayCommand]
    private async Task CopyCommands()
    {
        if (string.IsNullOrEmpty(Commands)) return;
        if (Owner is not { } owner) return;
        var clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetTextAsync(Commands);
        AddLog("✔ 已复制等效命令到剪贴板。");
    }

    /// <summary>提权重启（转发宿主）。</summary>
    [RelayCommand]
    private void Elevate() => _host.ElevateRestart();

    private DiagnosisReport? EnsureReported()
    {
        if (_report is null)
        {
            AddLog("✘ 请先执行诊断。");
        }
        return _report;
    }
}
