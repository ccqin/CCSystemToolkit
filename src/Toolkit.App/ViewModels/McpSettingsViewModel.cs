using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toolkit.Shell.Mcp;

namespace Toolkit.App.ViewModels;

/// <summary>
/// 「MCP 服务」设置弹窗 VM：聚合网关（单端口单 URL，启停/端口/接入链接）
/// + 各工具能力开关（启停只影响该能力的工具是否出现在网关清单）+ 接入授权（令牌）管理。
/// </summary>
public partial class McpSettingsViewModel : ObservableObject
{
    /// <summary>宿主窗口（剪贴板用，弹窗 Opened 时注入）。</summary>
    public Window? Owner { get; set; }

    /// <summary>各工具能力（网关聚合它们的工具）。</summary>
    public ObservableCollection<CapabilityViewModel> Capabilities { get; } = [];

    /// <summary>接入项（授权页列表）。</summary>
    public ObservableCollection<McpAccessItemViewModel> AccessItems { get; } = [];

    /// <summary>接入链接（服务页展示：网关 URL + 令牌）。</summary>
    public ObservableCollection<McpAccessLinkViewModel> AccessLinks { get; } = [];

    [ObservableProperty] private string _newAccessName = "";
    [ObservableProperty] private string _message = "";

    // ===== 网关状态 =====

    [ObservableProperty] private string _stateText = "○ 已停止";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _portInput = "";

    public string StateTag => IsRunning ? "Running" : "Stopped";

    /// <summary>网关端点 URL（不含令牌）。</summary>
    public string EndpointUrl => McpGatewayRuntime.Instance.EndpointUrl;

    public long TotalCalls => McpGatewayRuntime.Instance.TotalCalls;

    public McpSettingsViewModel()
    {
        foreach (var s in McpServiceRegistry.All)
            Capabilities.Add(new CapabilityViewModel(this, s));
        ReloadAccess();
        RefreshGateway();
        McpAccessStore.Changed += OnAccessChanged;
        McpGatewayRuntime.Instance.StateChanged += OnGatewayChanged;
    }

    /// <summary>弹窗关闭时退订（网关与能力运行时不受影响）。</summary>
    public void Cleanup()
    {
        McpAccessStore.Changed -= OnAccessChanged;
        McpGatewayRuntime.Instance.StateChanged -= OnGatewayChanged;
        foreach (var c in Capabilities) c.Detach();
    }

    private void OnGatewayChanged() => Dispatcher.UIThread.Post(RefreshGateway);

    private void OnAccessChanged() => Dispatcher.UIThread.Post(ReloadAccess);

    private void RefreshGateway()
    {
        var gateway = McpGatewayRuntime.Instance;
        IsRunning = gateway.IsRunning;
        StateText = IsRunning ? "● 运行中" : "○ 已停止";
        if (IsRunning || string.IsNullOrWhiteSpace(PortInput))
            PortInput = gateway.Port.ToString();
        OnPropertyChanged(nameof(StateTag));
        OnPropertyChanged(nameof(EndpointUrl));
        OnPropertyChanged(nameof(TotalCalls));
        ReloadAccess();   // 端口可能变化，接入链接随之重建
    }

    internal void ReloadAccess()
    {
        AccessItems.Clear();
        AccessLinks.Clear();
        var url = McpGatewayRuntime.Instance.EndpointUrl;
        foreach (var i in McpAccessStore.Items)
        {
            AccessItems.Add(new McpAccessItemViewModel { Model = i });
            AccessLinks.Add(new McpAccessLinkViewModel(this, i, url));
        }
    }

    internal void Notify(string message) => Message = message;

    internal async Task CopyToClipboard(string text, string what)
    {
        if (Owner is not { } owner) return;
        if (TopLevel.GetTopLevel(owner)?.Clipboard is { } clip)
        {
            await clip.SetTextAsync(text);
            Notify($"已复制{what}");
        }
    }

    // ===== 网关启停 =====

    [RelayCommand]
    private void StartGateway()
    {
        var port = int.TryParse(PortInput, out var p) && p is >= 1 and <= 65535 ? p : McpGatewayRuntime.Instance.DefaultPort;
        try
        {
            McpGatewayRuntime.Instance.Start(port);
            Notify($"网关已启动：{McpGatewayRuntime.Instance.EndpointUrl}——AI 客户端配置这一条 URL 即可使用全部启用的工具");
        }
        catch (Exception ex)
        {
            Notify("✘ " + ex.Message);
        }
        RefreshGateway();
    }

    [RelayCommand]
    private void StopGateway()
    {
        McpGatewayRuntime.Instance.Stop();
        Notify("网关已停止（所有 MCP 工具不可用）");
        RefreshGateway();
    }

    [RelayCommand]
    private Task CopyServiceUrl() =>
        CopyToClipboard(McpGatewayRuntime.Instance.EndpointUrl, "服务地址（不含令牌）");

    // ===== 接入授权 =====

    [RelayCommand]
    private void AddAccess()
    {
        var item = McpAccessStore.AddItem(NewAccessName);
        NewAccessName = "";
        Notify($"已新增接入「{item.Name}」；到「服务」页复制含令牌的接入 URL 给该 AI 客户端即可。");
    }

    [RelayCommand]
    private void RemoveAccess(McpAccessItemViewModel? item)
    {
        if (item is null) return;
        McpAccessStore.RemoveItem(item.Model.Id);
        Notify($"已删除接入「{item.Model.Name}」，其令牌立即失效。");
    }

    [RelayCommand]
    private Task CopyToken(McpAccessItemViewModel? item) =>
        item is null ? Task.CompletedTask : CopyToClipboard(item.Model.Token, "令牌");
}

/// <summary>网关聚合的一个「能力」行 VM（启停开关 + 工具数 + 调用统计）。</summary>
public partial class CapabilityViewModel : ObservableObject
{
    private readonly McpSettingsViewModel _owner;

    public CapabilityViewModel(McpSettingsViewModel owner, IMcpServiceDefinition service)
    {
        _owner = owner;
        Service = service;
        service.StateChanged += OnStateChanged;
        Refresh();
    }

    public IMcpServiceDefinition Service { get; }

    public string Name => Service.Name;
    public string Description => Service.Description;
    public long TotalCalls => Service.TotalCalls;
    public int ToolCount => Service.BuildTools().Count;

    public bool Enabled
    {
        get => Service.Enabled;
        set => Service.SetEnabled(value);
    }

    public void Detach() => Service.StateChanged -= OnStateChanged;

    private void OnStateChanged() => Dispatcher.UIThread.Post(Refresh);

    public void Refresh()
    {
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(TotalCalls));
        OnPropertyChanged(nameof(ToolCount));
    }
}

/// <summary>接入链接行 VM：某接入项在网关上的完整 URL（含令牌）。</summary>
public partial class McpAccessLinkViewModel : ObservableObject
{
    private readonly McpSettingsViewModel _owner;
    private readonly McpAccessItem _item;

    public McpAccessLinkViewModel(McpSettingsViewModel owner, McpAccessItem item, string baseUrl)
    {
        _owner = owner;
        _item = item;
        Url = baseUrl + "?token=" + item.Token;
    }

    public string Name => _item.Name;

    /// <summary>含令牌的完整接入 URL。</summary>
    public string Url { get; }

    public bool Enabled => _item.Enabled;

    public string EnabledText => _item.Enabled ? "" : "已停用";

    public string EnabledTag => _item.Enabled ? "Enabled" : "Disabled";

    [RelayCommand]
    private Task CopyUrl() =>
        _owner.CopyToClipboard(Url, $"「{Name}」的接入 URL" + (_item.Enabled ? "" : "（注意：该接入已停用）"));
}

/// <summary>接入授权行 VM（「接入授权」页列表）。</summary>
public partial class McpAccessItemViewModel : ObservableObject
{
    public required McpAccessItem Model { get; init; }

    public string Name => Model.Name;
    public string Token => Model.Token;
    public long CallCount => Model.CallCount;
    public string LastUsedText => Model.LastUsedAt?.ToString("MM-dd HH:mm:ss") ?? "从未使用";

    public bool Enabled
    {
        get => Model.Enabled;
        set
        {
            if (Model.Enabled == value) return;
            McpAccessStore.SetEnabled(Model.Id, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(EnabledText));
        }
    }

    public string EnabledText => Model.Enabled ? "启用" : "已停用";
}
