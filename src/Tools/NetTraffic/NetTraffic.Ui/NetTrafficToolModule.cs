using Avalonia.Controls;
using NetTraffic.Ui.ViewModels;
using NetTraffic.Ui.Views;
using Toolkit.Shell;

namespace NetTraffic.Ui;

/// <summary>流量监控工具模块：实时查看哪个进程在下载/上传（内核 ETW 归因）。</summary>
public sealed class NetTrafficToolModule : ToolModule
{
    /// <summary>工具显示名（统一日志来源标记）。</summary>
    public const string ToolTitle = "流量监控";

    public override string Id => "nettraffic";
    public override string Title => ToolTitle;
    public override string Glyph => "📶";
    public override string Description =>
        "实时查看每个进程的下载/上传速率与会话累计流量（内核 ETW TCP+UDP 事件按进程归因，任务管理器同源机制）。"
        + "发现“谁在偷流量”：按下载速率排序、最活跃进程高亮、TCP 连接数。需要管理员权限。";

    public override UserControl CreateView() => new NetTrafficView { DataContext = new NetTrafficViewModel(Host) };
}
