using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SerialMcpTool.Core;
using Toolkit.Shell;

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
        foreach (var item in _rt.Snapshot())
            Items.Add(new SerialItemViewModel { Model = item });
        RefreshItems();
        RefreshPorts();
        RefreshStatus();
        _rt.Traffic += OnTraffic;
        _rt.StateChanged += OnStateChanged;
    }

    /// <summary>视图脱离时退订（运行时仍在后台运行）。</summary>
    public void Detach()
    {
        if (_detached) return;
        _detached = true;
        _rt.Traffic -= OnTraffic;
        _rt.StateChanged -= OnStateChanged;
    }

    // ===== 串口设置 =====

    public ObservableCollection<string> PortNames { get; } = [];

    [ObservableProperty] private string? _selectedPort;
    [ObservableProperty] private int _baudRate = 115200;
    [ObservableProperty] private int _dataBitsIndex = 1;   // 默认 8
    [ObservableProperty] private int _parityIndex;
    [ObservableProperty] private int _stopBitsIndex;

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

    [ObservableProperty] private bool _hexMode;
    [ObservableProperty] private bool _autoScroll = true;
    [ObservableProperty] private string _statsText = "↑ 0 字节  ↓ 0 字节 · 0 条";

    /// <summary>新条目加入时触发（视图滚动到底部用）。</summary>
    public event Action<SerialItemViewModel>? ItemAdded;

    // ===== 手动测试 =====

    [ObservableProperty] private string _manualText = "";
    [ObservableProperty] private bool _manualHex;

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
                });
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
            _rt.Start(0);   // 0 = 用记忆端口/默认端口
            _host.Log(SerialMcpToolModule.ToolTitle, $"✔ MCP 服务器已启动：{_rt.EndpointUrl}");
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
        _rt.Stop();
        _host.Log(SerialMcpToolModule.ToolTitle, "MCP 服务器已停止");
        RefreshStatus();
    }

    [RelayCommand]
    private void OpenMcpSettings() => _host.ShowMcpSettings();

    [RelayCommand]
    private async Task CopyMcpUrl()
    {
        if (!_rt.IsRunning)
        {
            _host.Log(SerialMcpToolModule.ToolTitle, "△ MCP 服务未启动，先点击「启动服务」");
            return;
        }
        if (Owner is not { } owner) return;
        if (TopLevel.GetTopLevel(owner)?.Clipboard is { } clip)
        {
            await clip.SetTextAsync(_rt.EndpointUrl);
            _host.Log(SerialMcpToolModule.ToolTitle, "已复制服务地址（不含令牌；含令牌的接入 URL 在顶栏「MCP 服务」中复制）");
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        Items.Clear();
        _rt.ClearHistory();
        RefreshStatus();
    }

    [RelayCommand]
    private void ManualSend()
    {
        var text = ManualText;
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            var bytes = ManualHex ? SerialText.HexToBytes(text) : SerialText.UnescapeText(text);
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
        McpRunning = _rt.IsRunning;
        McpStatusText = _rt.IsRunning ? "运行中" : "未启动";
        McpUrl = _rt.IsRunning ? _rt.EndpointUrl : "";
        OnPropertyChanged(nameof(McpStateTag));
        StatsText = $"↑ 已发送 {serial.TotalSent} 字节  ↓ 已接收 {serial.TotalReceived} 字节 · {Items.Count} 条";
    }
}
