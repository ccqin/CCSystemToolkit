using Avalonia.Controls;
using Toolkit.App.ViewModels;
using Toolkit.App.Views;
using Toolkit.Shell;

namespace Toolkit.App.Tools;

/// <summary>
/// 操作日志工具模块：把宿主统一日志做成主页卡片（可进入的完整页面）。
/// 日志数据在宿主 MainViewModel 中，本模块直接引用它组装视图。
/// </summary>
public sealed class LogsToolModule : ToolModule
{
    private readonly MainViewModel _main;

    public LogsToolModule(MainViewModel main) => _main = main;

    public override string Id => "logs";
    public override string Title => "操作日志";
    public override string Glyph => "📋";
    public override string Description =>
        "实时查看所有工具归集的操作日志：按级别、来源筛选，统计成功/失败条数，一键清空。";

    public override UserControl CreateView() => new LogsView { DataContext = new LogsViewModel(_main) };
}
