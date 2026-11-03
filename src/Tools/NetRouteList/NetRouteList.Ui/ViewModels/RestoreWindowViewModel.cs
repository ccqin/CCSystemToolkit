using CommunityToolkit.Mvvm.ComponentModel;
using NetRouteList.Core.Models;
using NetRouteList.Core.Services;

namespace NetRouteList.Ui.ViewModels;

/// <summary>恢复选项对话框的 ViewModel。</summary>
public partial class RestoreWindowViewModel : ObservableObject
{
    public required BackupSnapshot Snapshot { get; init; }
    public string BackupName => Snapshot.Name;
    public string BackupMeta => $"{Snapshot.CreatedAtLocal:yyyy-MM-dd HH:mm:ss} · IPv4 {Snapshot.Ipv4RouteCount} 条";

    [ObservableProperty] private bool _isMerge = true;
    [ObservableProperty] private bool _isExact;
    [ObservableProperty] private bool _includeIpv6;
    [ObservableProperty] private bool _skipSystemRoutes = true;
    [ObservableProperty] private bool _safetySnapshot = true;
    [ObservableProperty] private string _errorText = "";

    public RestoreOptions BuildOptions() => new()
    {
        Mode = IsExact ? RestoreMode.Exact : RestoreMode.Merge,
        IncludeIpv6 = IncludeIpv6,
        SkipSystemRoutes = SkipSystemRoutes,
        SafetySnapshot = SafetySnapshot,
    };
}
