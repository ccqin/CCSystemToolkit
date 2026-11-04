using Avalonia.Controls;
using MenuClean.Ui.ViewModels;
using MenuClean.Ui.Views;
using Toolkit.Shell;

namespace MenuClean.Ui;

/// <summary>清理右键菜单工具模块：按场景查看/禁用/删除右键菜单注入项。</summary>
public sealed class MenuCleanToolModule : ToolModule
{
    /// <summary>工具显示名（统一日志来源标记）。</summary>
    public const string ToolTitle = "清理右键菜单";

    public override string Id => "menuclean";
    public override string Title => ToolTitle;
    public override string Glyph => "🧹";
    public override string Description =>
        "按「右键场景」（文件/文件夹/空白处/驱动器/后缀）模拟查看真实菜单；检测软件卸载残留与第三方注入，一键禁用（可逆）或清理（自动 .reg 备份）；按来源软件批量管理。";

    public override UserControl CreateView() => new MenuCleanView { DataContext = new MenuCleanViewModel(Host) };
}
