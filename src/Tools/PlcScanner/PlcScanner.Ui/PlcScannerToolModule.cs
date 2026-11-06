using Avalonia.Controls;
using PlcScanner.Ui.ViewModels;
using PlcScanner.Ui.Views;
using Toolkit.Shell;

namespace PlcScanner.Ui;

/// <summary>PLC IP 扫描工具模块（WinForms 版 PlcIpScanner 迁移）。</summary>
public sealed class PlcScannerToolModule : ToolModule
{
    /// <summary>工具显示名（统一日志来源标记）。</summary>
    public const string ToolTitle = "PLC IP 扫描";

    public override string Id => "plcscanner";
    public override string Title => ToolTitle;
    public override string Glyph => "📡";
    public override string Description =>
        "ARP 扫描网段发现设备（PLC/工控机等）：网卡源地址自动选择（多网卡同网段不漏设备）、支持 CIDR/范围/多段、TCP 端口探测（21+23 识别疑似 SC 设备）、自动清理失效 ARP 缓存、进度与预计剩余时间、双击复制 IP。";

    public override UserControl CreateView() => new PlcScannerView { DataContext = new PlcScannerViewModel(Host) };
}
