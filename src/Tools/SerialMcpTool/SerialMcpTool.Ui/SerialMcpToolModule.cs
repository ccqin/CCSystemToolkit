using Avalonia.Controls;
using SerialMcpTool.Core;
using SerialMcpTool.Ui.ViewModels;
using SerialMcpTool.Ui.Views;
using Toolkit.Shell;
using Toolkit.Shell.Mcp;

namespace SerialMcpTool.Ui;

/// <summary>串口 MCP 工具模块：卡片内配置串口并实时监控 AI 与串口的收发内容。</summary>
public sealed class SerialMcpToolModule : ToolModule
{
    /// <summary>工具显示名（统一日志来源标记）。</summary>
    public const string ToolTitle = "串口 MCP";

    static SerialMcpToolModule() => McpServiceRegistry.Register(SerialMcpRuntime.Instance);

    public override string Id => "serialmcp";
    public override string Title => ToolTitle;
    public override string Glyph => "🔌";

    public override string Description =>
        "内置串口 MCP 服务器：AI 客户端通过 MCP 协议打开串口、收发数据，卡片实时显示 AI 下发与串口返回的内容；支持手动发送测试与 HEX 显示。服务启停/接入令牌在顶栏「MCP 服务」中统一管理。";

    public override UserControl CreateView() => new SerialMcpView { DataContext = new SerialMcpViewModel(Host) };
}
