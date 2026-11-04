using Avalonia.Controls;
using ShellExt.Ui.ViewModels;
using ShellExt.Ui.Views;
using Toolkit.Shell;

namespace ShellExt.Ui;

/// <summary>COM 扩展管理器工具模块：枚举/禁用/启用 Shell 扩展（右键菜单动态项、图标叠加、缩略图、预览）。</summary>
public sealed class ShellExtToolModule : ToolModule
{
    /// <summary>工具显示名（统一日志来源标记）。</summary>
    public const string ToolTitle = "COM 扩展管理";

    public override string Id => "shellext";
    public override string Title => ToolTitle;
    public override string Glyph => "🧩";
    public override string Description =>
        "管理 COM Shell 扩展：右键菜单的动态项（如“发送到手机”）、图标叠加、缩略图、预览窗格；带 Approved 官方描述可搜索，DLL 残留检测，黑名单禁用可逆。";

    public override UserControl CreateView() => new ShellExtView { DataContext = new ShellExtViewModel(Host) };
}
