using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toolkit.Shell.Mcp;

namespace Toolkit.App.ViewModels;

/// <summary>「MCP 服务」设置弹窗 VM：各 MCP 服务启停/端口 + AI 客户端接入授权（令牌）管理。</summary>
public partial class McpSettingsViewModel : ObservableObject
{
    /// <summary>宿主窗口（剪贴板用，弹窗 Opened 时注入）。</summary>
    public Window? Owner { get; set; }

    public ObservableCollection<McpServiceViewModel> Services { get; } = [];
    public ObservableCollection<McpAccessItemViewModel> AccessItems { get; } = [];

    [ObservableProperty] private string _newAccessName = "";
    [ObservableProperty] private string _message = "";

    public McpSettingsViewModel()
    {
        foreach (var s in McpServiceRegistry.All)
            Services.Add(new McpServiceViewModel(this, s));
        ReloadAccess();
        McpAccessStore.Changed += OnAccessChanged;
    }

    /// <summary>弹窗关闭时退订（服务运行时不受影响）。</summary>
    public void Cleanup()
    {
        McpAccessStore.Changed -= OnAccessChanged;
        foreach (var s in Services) s.Detach();
    }

    private void OnAccessChanged() => Dispatcher.UIThread.Post(ReloadAccess);

    internal void ReloadAccess()
    {
        AccessItems.Clear();
        foreach (var i in McpAccessStore.Items)
            AccessItems.Add(new McpAccessItemViewModel { Model = i });
        foreach (var s in Services) s.RefreshLinks();
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

/// <summary>一个 MCP 服务的设置行 VM（端口输入 + 启停 + 接入链接列表）。</summary>
public partial class McpServiceViewModel : ObservableObject
{
    private readonly McpSettingsViewModel _owner;

    public McpServiceViewModel(McpSettingsViewModel owner, IMcpServiceDefinition service)
    {
        _owner = owner;
        Service = service;
        service.StateChanged += OnStateChanged;
        Refresh();
        RefreshLinks();
    }

    public IMcpServiceDefinition Service { get; }

    public string Name => Service.Name;
    public string Description => Service.Description;
    public string EndpointUrl => Service.EndpointUrl;
    public long TotalCalls => Service.TotalCalls;
    public string StateTag => Service.IsRunning ? "Running" : "Stopped";

    [ObservableProperty] private string _stateText = "○ 已停止";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _portInput = "";

    /// <summary>该服务的接入链接（含令牌，随接入授权数据刷新；集合实例稳定）。</summary>
    public ObservableCollection<McpAccessLinkViewModel> AccessLinks { get; } = [];

    public bool NoAccessLinks => AccessLinks.Count == 0;

    public void Detach() => Service.StateChanged -= OnStateChanged;

    private void OnStateChanged() => Dispatcher.UIThread.Post(Refresh);

    public void Refresh()
    {
        IsRunning = Service.IsRunning;
        StateText = IsRunning ? "● 运行中" : "○ 已停止";
        if (IsRunning || string.IsNullOrWhiteSpace(PortInput))
            PortInput = Service.Port.ToString();
        OnPropertyChanged(nameof(StateTag));
        OnPropertyChanged(nameof(EndpointUrl));
        OnPropertyChanged(nameof(TotalCalls));
    }

    public void RefreshLinks()
    {
        AccessLinks.Clear();
        foreach (var i in McpAccessStore.Items)
            AccessLinks.Add(new McpAccessLinkViewModel(_owner, i, Service));
        OnPropertyChanged(nameof(NoAccessLinks));
    }

    [RelayCommand]
    private void StartService()
    {
        var port = int.TryParse(PortInput, out var p) && p is >= 1 and <= 65535 ? p : Service.DefaultPort;
        try
        {
            Service.Start(port);
            _owner.Notify($"已启动「{Service.Name}」：{Service.EndpointUrl}");
        }
        catch (Exception ex)
        {
            _owner.Notify("✘ " + ex.Message);
        }
        Refresh();
    }

    [RelayCommand]
    private void StopService()
    {
        Service.Stop();
        _owner.Notify($"已停止「{Service.Name}」");
        Refresh();
    }

    [RelayCommand]
    private Task CopyServiceUrl() =>
        _owner.CopyToClipboard(Service.EndpointUrl, "服务地址（不含令牌）");
}

/// <summary>接入链接行 VM：某接入项在某服务上的完整 URL（含令牌）。</summary>
public partial class McpAccessLinkViewModel : ObservableObject
{
    private readonly McpSettingsViewModel _owner;
    private readonly McpAccessItem _item;
    private readonly IMcpServiceDefinition _service;

    public McpAccessLinkViewModel(McpSettingsViewModel owner, McpAccessItem item, IMcpServiceDefinition service)
    {
        _owner = owner;
        _item = item;
        _service = service;
    }

    public string Name => _item.Name;

    /// <summary>含令牌的完整接入 URL。</summary>
    public string Url => _service.EndpointUrl + "?token=" + _item.Token;

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
