using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SerialMcpTool.Core;
using Toolkit.Shell;
using Toolkit.Shell.Mcp;

namespace SerialMcpTool.Ui.ViewModels;

/// <summary>视图 XAML 用的小转换器（与宿主 Converters 同一模式：x:Static 直接引用）。</summary>
public static class Converters
{
    /// <summary>集合数量 → 是否为空（用于监控列表空态提示）。</summary>
    public static readonly FuncValueConverter<int, bool> CountIsZero =
        new(c => c == 0);
}

/// <summary>收发监控行 VM（HEX 显示切换时重算显示文本）。</summary>
public partial class SerialItemViewModel : ObservableObject
{
    public required SerialTrafficItem Model { get; init; }

    public string TimeText => Model.TimeText;

    /// <summary>方向标识（样式选择器按 Tag 着色）。</summary>
    public string Tag => Model.Direction.ToString();

    public string DirectionText => Model.Direction switch
    {
        SerialDirection.AiToPort => "AI→串口",
        SerialDirection.ManualToPort => "手动→串口",
        SerialDirection.FromPort => "串口→返回",
        SerialDirection.Info => "系统",
        SerialDirection.AiCall => "AI调用",
        SerialDirection.Error => "错误",
        _ => "",
    };

    [ObservableProperty] private string _displayText = "";

    public void Refresh(bool hex) => DisplayText = Model.Data is { } d
        ? (hex ? SerialText.ToHex(d) : SerialText.EscapeText(d))
        : Model.Message;
}

/// <summary>
/// 串口 MCP 工具主 VM：串口参数与开关、MCP 服务状态（快捷启停）、收发监控列表、手动发送测试。
/// 串口与服务器状态由 <see cref="SerialMcpRuntime"/> 单例承载，切换视图不中断。
/// </summary>
public partial class SerialMcpViewModel : ObservableObject
{
    private const int MaxItems = 1000;
    private readonly IToolHost _host;
    private readonly SerialMcpRuntime _rt = SerialMcpRuntime.Instance;
    private bool _detached;

    public static readonly IReadOnlyList<int> BaudRates =
        [1200, 2400, 4800, 9600, 14400, 19200, 38400, 57600, 115200, 230400, 460800, 921600];
    public static readonly IReadOnlyList<string> Parities = ["无校验", "偶校验", "奇校验"];
    public static readonly IReadOnlyList<string> StopBits = ["1", "1.5", "2"];
    public static readonly IReadOnlyList<int> DataBits = [7, 8];

    /// <summary>宿主窗口（剪贴板用，视图注入）。</summary>
    public Window? Owner { get; set; }

    public SerialMcpViewModel(IToolHost host)
    {
        _host = host;
        FilteredItems = new DataGridCollectionView(Items) { Filter = FilterItem };
        foreach (var item in _rt.Snapshot())
            Items.Add(new SerialItemViewModel { Model = item });
        RefreshItems();
        RefreshPorts();
        RefreshStatus();
        _rt.Traffic += OnTraffic;
        _rt.StateChanged += OnStateChanged;
        McpGatewayRuntime.Instance.StateChanged += OnStateChanged;
    }

    partial void OnFilterAllChanged(bool value)
    {
        if (_syncingFilters) return;
        _syncingFilters = true;
        FilterFromPort = FilterAiToPort = FilterAiCall = FilterManualToPort = FilterInfo = FilterError = value;
        _syncingFilters = false;
        FilteredItems.Refresh();
    }

    partial void OnFilterFromPortChanged(bool value) => FilterFlagChanged();
    partial void OnFilterAiToPortChanged(bool value) => FilterFlagChanged();
    partial void OnFilterAiCallChanged(bool value) => FilterFlagChanged();
    partial void OnFilterManualToPortChanged(bool value) => FilterFlagChanged();
    partial void OnFilterInfoChanged(bool value) => FilterFlagChanged();
    partial void OnFilterErrorChanged(bool value) => FilterFlagChanged();

    private void FilterFlagChanged()
    {
        if (_syncingFilters) return;
        // 六个方向全勾时"全部"自动勾上；否则去掉（避免"全部"与其余勾选状态矛盾）
        var all = FilterFromPort && FilterAiToPort && FilterAiCall && FilterManualToPort && FilterInfo && FilterError;
        if (FilterAll != all)
        {
            _syncingFilters = true;
            FilterAll = all;
            _syncingFilters = false;
        }
        FilteredItems.Refresh();
    }

    private bool FilterItem(object obj) => obj is not SerialItemViewModel vm || vm.Model.Direction switch
    {
        SerialDirection.FromPort => FilterFromPort,
        SerialDirection.AiToPort => FilterAiToPort,
        SerialDirection.AiCall => FilterAiCall,
        SerialDirection.ManualToPort => FilterManualToPort,
        SerialDirection.Info => FilterInfo,
        SerialDirection.Error => FilterError,
        _ => true,
    };

    /// <summary>视图脱离时退订（运行时仍在后台运行）。</summary>
    public void Detach()
    {
        if (_detached) return;
        _detached = true;
        _rt.Traffic -= OnTraffic;
        _rt.StateChanged -= OnStateChanged;
        McpGatewayRuntime.Instance.StateChanged -= OnStateChanged;
    }

    // ===== 串口设置 =====

    public ObservableCollection<string> PortNames { get; } = [];

    [ObservableProperty] private string? _selectedPort;
    [ObservableProperty] private int _baudRate = 115200;

    /// <summary>波特率输入（可从常用列表选，也可手动输入任意值；合法整数时同步到 BaudRate）。</summary>
    [ObservableProperty] private string _baudRateText = "115200";

    partial void OnBaudRateTextChanged(string value)
    {
        if (int.TryParse(value?.Trim(), out var b) && b is > 0 and <= 10_000_000)
            BaudRate = b;
    }
    [ObservableProperty] private int _dataBitsIndex = 1;   // 默认 8
    [ObservableProperty] private int _parityIndex;
    [ObservableProperty] private int _stopBitsIndex;

    /// <summary>行历史保留条数（0 = 全部缓存；打开串口时生效）。</summary>
    [ObservableProperty] private string _historyInput = "0";

    [ObservableProperty] private bool _portOpen;
    [ObservableProperty] private string _portStatusText = "串口未打开";

    // ===== MCP 服务状态 =====

    [ObservableProperty] private bool _mcpRunning;
    [ObservableProperty] private string _mcpStatusText = "未启动";
    [ObservableProperty] private string _mcpUrl = "";

    /// <summary>状态样式标记（mcpState 选择器按 Tag 着色）。</summary>
    public string McpStateTag => McpRunning ? "Running" : "Stopped";

    // ===== 收发监控 =====

    public ObservableCollection<SerialItemViewModel> Items { get; } = [];

    /// <summary>过滤后的监控视图（方向标签勾选过滤，列表绑定这个）。</summary>
    public DataGridCollectionView FilteredItems { get; }

    /// <summary>列表多选（SelectionMode=Multiple,Toggle 绑定）。</summary>
    public ObservableCollection<SerialItemViewModel> SelectedItems { get; } = [];

    [ObservableProperty] private bool _hexMode;
    [ObservableProperty] private bool _autoScroll = true;
    [ObservableProperty] private string _statsText = "↑ 0 字节  ↓ 0 字节 · 0 条";

    /// <summary>新条目加入时触发（视图滚动到底部用）。</summary>
    public event Action<SerialItemViewModel>? ItemAdded;

    // ===== 方向过滤标签（默认全选=全部显示；只勾某几个则只显示对应方向） =====

    [ObservableProperty] private bool _filterAll = true;
    [ObservableProperty] private bool _filterFromPort = true;
    [ObservableProperty] private bool _filterAiToPort = true;
    [ObservableProperty] private bool _filterAiCall = true;
    [ObservableProperty] private bool _filterManualToPort = true;
    [ObservableProperty] private bool _filterInfo = true;
    [ObservableProperty] private bool _filterError = true;

    private bool _syncingFilters;

    // ===== 手动测试 =====

    [ObservableProperty] private string _manualText = "";
    [ObservableProperty] private bool _manualHex;

    /// <summary>文本模式发送末尾自动附加 \r\n（多数设备/终端需要换行才应答）；HEX 模式不附加。</summary>
    [ObservableProperty] private bool _appendNewline = true;

    partial void OnHexModeChanged(bool value) => RefreshItems();

    [RelayCommand]
    private void RefreshPorts()
    {
        var names = SerialPortService.GetPortNames();
        PortNames.Clear();
        foreach (var n in names) PortNames.Add(n);
        if (SelectedPort is null || !names.Contains(SelectedPort, StringComparer.OrdinalIgnoreCase))
            SelectedPort = names.FirstOrDefault();
        RefreshStatus();
    }

    [RelayCommand]
    private void OpenPort()
    {
        if (SelectedPort is not { } port)
        {
            _host.Log(SerialMcpToolModule.ToolTitle, "△ 请先选择串口（本机未检测到串口时点击「刷新」）");
            return;
        }
        var history = int.TryParse(HistoryInput, out var h) && h >= 0 ? h : 0;
        try
        {
            _rt.Serial.Open(port, BaudRate, DataBits[DataBitsIndex],
                ParityIndex switch
                {
                    1 => System.IO.Ports.Parity.Even,
                    2 => System.IO.Ports.Parity.Odd,
                    _ => System.IO.Ports.Parity.None,
                },
                StopBitsIndex switch
                {
                    1 => System.IO.Ports.StopBits.OnePointFive,
                    2 => System.IO.Ports.StopBits.Two,
                    _ => System.IO.Ports.StopBits.One,
                }, history);
            _host.Log(SerialMcpToolModule.ToolTitle, $"✔ 已打开串口 {port} @ {BaudRate}");
        }
        catch (Exception ex)
        {
            _host.Log(SerialMcpToolModule.ToolTitle, "✘ " + ex.Message);
            _rt.Serial.RaiseError(ex.Message);
        }
        RefreshStatus();
    }

    [RelayCommand]
    private void ClosePort()
    {
        if (!_rt.Serial.IsOpen)
        {
            _host.Log(SerialMcpToolModule.ToolTitle, "△ 串口未打开");
            return;
        }
        _rt.Serial.Close();
        _host.Log(SerialMcpToolModule.ToolTitle, "✔ 已关闭串口");
        RefreshStatus();
    }

    [RelayCommand]
    private void StartMcp()
    {
        try
        {
            McpGatewayRuntime.Instance.Start();   // 聚合网关：单端口暴露全部已启用能力
            _rt.SetEnabled(true);
            _host.Log(SerialMcpToolModule.ToolTitle, $"✔ MCP 网关已启动：{McpGatewayRuntime.Instance.EndpointUrl}（串口工具已启用）");
        }
        catch (Exception ex)
        {
            _host.Log(SerialMcpToolModule.ToolTitle, "✘ " + ex.Message);
            _rt.Serial.RaiseError(ex.Message);
        }
        RefreshStatus();
    }

    [RelayCommand]
    private void StopMcp()
    {
        _rt.SetEnabled(false);
        _host.Log(SerialMcpToolModule.ToolTitle, "串口 MCP 工具已停用（网关与其他工具不受影响，整体启停在顶栏「MCP 服务」）");
        RefreshStatus();
    }

    [RelayCommand]
    private void OpenMcpSettings() => _host.ShowMcpSettings();

    [RelayCommand]
    private async Task CopyMcpUrl()
    {
        if (!McpGatewayRuntime.Instance.IsRunning)
        {
            _host.Log(SerialMcpToolModule.ToolTitle, "△ MCP 网关未启动，先点击「启动服务」");
            return;
        }
        if (Owner is not { } owner) return;
        if (TopLevel.GetTopLevel(owner)?.Clipboard is { } clip)
        {
            await clip.SetTextAsync(McpGatewayRuntime.Instance.EndpointUrl);
            _host.Log(SerialMcpToolModule.ToolTitle, "已复制网关地址（不含令牌；含令牌的接入 URL 在顶栏「MCP 服务」中复制）");
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        Items.Clear();
        _rt.ClearHistory();
        RefreshStatus();
    }

    /// <summary>复制全部监控记录（时间 + 方向 + 内容，制表符分隔）——方便整段发给 AI 或存档分析。</summary>
    [RelayCommand]
    private async Task CopyAllLog()
    {
        if (Items.Count == 0)
        {
            _host.Log(SerialMcpToolModule.ToolTitle, "△ 监控列表为空，没有可复制的内容");
            return;
        }
        if (Owner is not { } owner) return;
        if (TopLevel.GetTopLevel(owner)?.Clipboard is not { } clip) return;
        var sb = new StringBuilder();
        foreach (var item in Items)
            sb.Append(item.TimeText).Append('\t').Append(item.DirectionText).Append('\t').AppendLine(item.DisplayText);
        await clip.SetTextAsync(sb.ToString());
        _host.Log(SerialMcpToolModule.ToolTitle, $"✔ 已复制 {Items.Count} 条监控记录");
    }

    /// <summary>复制选中的监控行（列表可多选：Ctrl 点选 / 点选切换）。</summary>
    [RelayCommand]
    private async Task CopySelected()
    {
        if (SelectedItems.Count == 0)
        {
            _host.Log(SerialMcpToolModule.ToolTitle, "△ 没有选中的行（Ctrl 点选或直接点选多行后再右键复制）");
            return;
        }
        if (Owner is not { } owner) return;
        if (TopLevel.GetTopLevel(owner)?.Clipboard is not { } clip) return;
        var sb = new StringBuilder();
        foreach (var item in SelectedItems)
            sb.Append(item.TimeText).Append('\t').Append(item.DirectionText).Append('\t').AppendLine(item.DisplayText);
        await clip.SetTextAsync(sb.ToString());
        _host.Log(SerialMcpToolModule.ToolTitle, $"✔ 已复制 {SelectedItems.Count} 条选中记录");
    }

    [RelayCommand]
    private void ManualSend()
    {
        var text = ManualText;
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            byte[] bytes;
            if (ManualHex)
            {
                bytes = SerialText.HexToBytes(text);
            }
            else
            {
                // 附加回车：末尾已带换行（真实字符或显式转义）时不重复附加
                if (AppendNewline
                    && !text.EndsWith('\r') && !text.EndsWith('\n')
                    && !text.EndsWith("\\r") && !text.EndsWith("\\n"))
                {
                    text += "\\r\\n";
                }
                bytes = SerialText.UnescapeText(text);
            }
            if (bytes.Length == 0)
            {
                _host.Log(SerialMcpToolModule.ToolTitle, "△ 手动发送内容为空");
                return;
            }
            _rt.Serial.Write(bytes, SerialDirection.ManualToPort);
            ManualText = "";
        }
        catch (Exception ex)
        {
            _host.Log(SerialMcpToolModule.ToolTitle, "✘ " + ex.Message);
            _rt.Serial.RaiseError(ex.Message);
        }
    }

    private void OnTraffic(SerialTrafficItem item)
    {
        if (_detached) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_detached) return;
            var vm = new SerialItemViewModel { Model = item };
            vm.Refresh(HexMode);
            Items.Add(vm);
            while (Items.Count > MaxItems) Items.RemoveAt(0);
            RefreshStatus();
            if (AutoScroll) ItemAdded?.Invoke(vm);
        });
    }

    private void OnStateChanged()
    {
        if (_detached) return;
        Dispatcher.UIThread.Post(RefreshStatus);
    }

    private void RefreshItems()
    {
        foreach (var item in Items) item.Refresh(HexMode);
    }

    private void RefreshStatus()
    {
        var serial = _rt.Serial;
        PortOpen = serial.IsOpen;
        PortStatusText = serial.IsOpen
            ? $"已打开 {serial.PortName} @ {serial.BaudRate}（{serial.SettingsText}）"
            : "串口未打开";
        var gateway = McpGatewayRuntime.Instance;
        McpRunning = gateway.IsRunning && _rt.Enabled;
        McpStatusText = !gateway.IsRunning ? "未启动" : _rt.Enabled ? "运行中" : "已停用";
        McpUrl = gateway.IsRunning ? gateway.EndpointUrl : "";
        OnPropertyChanged(nameof(McpStateTag));
        StatsText = $"↑ 已发送 {serial.TotalSent} 字节  ↓ 已接收 {serial.TotalReceived} 字节 · {Items.Count} 条";
    }
}
