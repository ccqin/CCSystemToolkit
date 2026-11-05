using Avalonia.Controls;
using StartupMgr.Ui.ViewModels;
using StartupMgr.Ui.Views;
using Toolkit.Shell;

namespace StartupMgr.Ui;

/// <summary>开机启动项管理工具模块。</summary>
public sealed class StartupMgrToolModule : ToolModule
{
    /// <summary>工具显示名（统一日志来源标记）。</summary>
    public const string ToolTitle = "开机启动项";

    public override string Id => "startupmgr";
    public override string Title => ToolTitle;
    public override string Glyph => "🚀";
    public override string Description =>
        "管理开机启动项：注册表 Run/RunOnce（HKCU/HKLM/32位视图）+ 启动文件夹（用户/全局）。禁用走系统 StartupApproved（与任务管理器互通、可逆），卸载残留检测，删除自动备份可还原，支持新增 HKCU·Run 项。";

    public override UserControl CreateView() => new StartupMgrView { DataContext = new StartupMgrViewModel(Host) };
}
