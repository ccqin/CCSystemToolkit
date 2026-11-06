using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.Input;
using PlcScanner.Core.Models;
using PlcScanner.Core.Services;
using Toolkit.Shell;

namespace PlcScanner.Ui.ViewModels;

/// <summary>PLC IP 扫描主 VM：网卡选择 → 网段 → ARP 扫描 → 端口探测 → 结果表。</summary>
public partial class PlcScannerViewModel : ObservableObject
{
    private readonly IToolHost _host;
    private CancellationTokenSource? _cts;
    private readonly HashSet<uint> _localIps = [];
    private readonly ConcurrentDictionary<uint, DeviceItem> _found = new();

    public PlcScannerViewModel(IToolHost host)
    {
        _host = host;
        LoadNics();
    }

    // ---------- 网卡 ----------
    public ObservableCollection<NicEntry> Nics { get; } = [];

    [ObservableProperty]
    private NicEntry? _selectedNic;

    [ObservableProperty]
    private bool _showVirtual;

    [ObservableProperty]
    private string _nicIpsText = "";

    // ---------- 网段 / 端口 ----------
    [ObservableProperty]
    private string _rangesText = "";

    [ObservableProperty]
    private bool _hideLocal = true;

    [ObservableProperty]
    private bool _probePorts = true;

    [ObservableProperty]
    private string _portsText = "23,21,102,502";

    // ---------- 扫描状态 ----------
    [ObservableProperty]
    private bool _scanning;

    [ObservableProperty]
    private int _scanned;

    [ObservableProperty]
    private int _total;

    [ObservableProperty]
    private string _etaText = "";

    [ObservableProperty]
    private string _statusText = "就绪";

    public Avalonia.Controls.Window? Owner { get; set; }

    // ---------- 结果 ----------
    public ObservableCollection<DeviceItem> Devices { get; } = [];

    public void AddLog(string message) => _host.Log(PlcScannerToolModule.ToolTitle, message);

    // ---------- 命令 ----------

    [RelayCommand]
    private void RefreshNics() => LoadNics();

    partial void OnShowVirtualChanged(bool value) => LoadNics();

    partial void OnSelectedNicChanged(NicEntry? value)
    {
        NicIpsText = value is null ? "" : ScanOrchestrator.NicIpsText(value);
        if (value is not null) SaveLastNic(value.Display);
    }

    private void LoadNics()
    {
        var prev = SelectedNic?.Display ?? LoadLastNic();
        Nics.Clear();
        foreach (var nic in ScanOrchestrator.LoadNics(ShowVirtual)) Nics.Add(nic);
        // 默认选择：上次选择 → “以太网 3”（接 PLC 的网卡）→ 第一个非自动 Up 物理网卡
        SelectedNic = Nics.FirstOrDefault(n => n.Display == prev)
                      ?? Nics.FirstOrDefault(n => n.Display.Contains("以太网 3"))
                      ?? Nics.FirstOrDefault(n => !n.Display.StartsWith("★") && n.Display.StartsWith("●"))
                      ?? Nics.FirstOrDefault();
        SaveLastNic(SelectedNic?.Display);
    }

    private static string SettingsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystemToolkit", "PlcScanner");

    private static string? LoadLastNic()
    {
        try
        {
            var f = Path.Combine(SettingsDir, "last-nic.txt");
            return File.Exists(f) ? File.ReadAllText(f).Trim() : null;
        }
        catch { return null; }
    }

    private static void SaveLastNic(string? display)
    {
        try
        {
            if (display is null) return;
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(Path.Combine(SettingsDir, "last-nic.txt"), display);
        }
        catch { }
    }

    [RelayCommand]
    private void FillNicRanges()
    {
        if (SelectedNic is not { } nic) return;
        var lines = RangesText.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        foreach (var cidr in ScanOrchestrator.NicRangesToCidr(nic))
            if (!lines.Contains(cidr)) lines.Add(cidr);
        RangesText = string.Join(Environment.NewLine, lines);
    }

    [RelayCommand]
    private async Task StartScan()
    {
        if (Scanning) return;
        if (SelectedNic is not { } nic || nic.Ipv4s.Count == 0)
        {
            StatusText = "请先选择有 IPv4 地址的网卡";
            return;
        }

        var (ranges, errors) = ScanOrchestrator.ParseRanges(RangesText);
        if (ranges.Count == 0)
        {
            StatusText = "网段为空或全部无法解析" + (errors.Count > 0 ? $"：{errors[0]}" : "");
            return;
        }
        long total = ranges.Sum(r => ScanCore.AddressCount(r.First, r.Last));
        if (total > 8192)
        {
            StatusText = $"共 {total} 个地址，过大（>8192），请缩小网段";
            return;
        }
        var ports = ScanCore_ConcatPorts();

        _localIps.Clear();
        foreach (var ip in ScanOrchestrator.GetAllLocalIps()) _localIps.Add(ip);
        _found.Clear();
        Devices.Clear();
        Scanned = 0;
        Total = (int)total;
        Scanning = true;
        StatusText = "ARP 扫描中...";
        var cts = _cts = new CancellationTokenSource();
        var startedAt = Environment.TickCount64;

        var errNote = errors.Count > 0 ? $"（{errors.Count} 段解析失败已跳过）" : "";
        try
        {
            await ScanOrchestrator.ScanAsync(
                nic, ranges, ports, ProbePorts, cts.Token,
                onStatus: s => Dispatcher.UIThread.Post(() => StatusText = s),
                onProgress: (scanned, total2) => Dispatcher.UIThread.Post(() =>
                {
                    Scanned = Math.Min(scanned, Total);
                    if (scanned > 5 && scanned < Total)
                    {
                        long elapsed = Environment.TickCount64 - startedAt;
                        long remain = elapsed * (Total - scanned) / scanned / 1000;
                        EtaText = remain >= 60 ? $"~{remain / 60}分{remain % 60}秒" : $"~{remain}秒";
                    }
                }),
                onFound: item => Dispatcher.UIThread.Post(() =>
                {
                    _found[item.IpNet] = item;
                    if (!item.IsLocal || !HideLocal) Devices.Add(item);
                }),
                onPortsDone: (ipNet, open) => Dispatcher.UIThread.Post(() =>
                {
                    if (_found.TryGetValue(ipNet, out var d))
                        d.ApplyPorts(open);
                }));

            if (errors.Count > 0) AddLog($"△ 扫描完成（{errNote}），发现 {_found.Count} 台设备。");
            else AddLog($"✔ 扫描完成，{_total} 个地址发现 {_found.Count} 台设备。");
        }
        finally
        {
            Scanning = false;
            EtaText = "";
            cts.Dispose();
            if (ReferenceEquals(_cts, cts)) _cts = null;
        }
    }

    private int[] ScanCore_ConcatPorts() => ScanOrchestrator.ParsePorts(PortsText);

    [RelayCommand]
    private void StopScan()
    {
        try { _cts?.Cancel(); } catch { }
        StatusText = "正在停止...";
    }

    /// <summary>对单个设备用当前端口框的端口重新探测（扫描后补测/换端口再测）。</summary>
    [RelayCommand]
    private async Task ProbeDevice(DeviceItem item)
    {
        var ports = ScanOrchestrator.ParsePorts(PortsText);
        if (ports.Length == 0)
        {
            StatusText = "端口列表为空或无法解析";
            return;
        }
        item.Ports = "…";
        StatusText = $"正在探测 {item.Ip}（端口 {string.Join(",", ports)}）...";
        int[] open = await ScanCore.ProbePortsAsync(item.Ip, ports, 800, CancellationToken.None);
        item.ApplyPorts(open);
        var scNote = item.IsScDevice ? " → " + item.Note : "";
        StatusText = open.Length > 0
            ? $"{item.Ip}：开放 {string.Join(",", open)}{scNote}"
            : $"{item.Ip}：指定端口均未开放（可改端口框后再试，如 102/502/44818）";
    }

    [RelayCommand]
    private void ClearResults()
    {
        _found.Clear();
        Devices.Clear();
    }

    partial void OnHideLocalChanged(bool value)
    {
        Devices.Clear();
        foreach (var d in _found.Values)
            if (!d.IsLocal || !value) Devices.Add(d);
    }

    /// <summary>双击行复制 IP。</summary>
    public async Task CopyIpAsync(DeviceItem item)
    {
        if (Owner is not { } owner) return;
        if (Avalonia.Controls.TopLevel.GetTopLevel(owner)?.Clipboard is not { } clip) return;
        await clip.SetTextAsync(item.Ip);
        StatusText = $"已复制 {item.Ip} 到剪贴板";
    }
}
