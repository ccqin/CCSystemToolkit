using Avalonia.Controls;
using PermDoctor.Core.Services;
using PermDoctor.Ui.ViewModels;
using PermDoctor.Ui.Views;
using Toolkit.Shell;

namespace PermDoctor.Ui;

/// <summary>权限医生工具模块：诊断/修复文件夹 ACL 权限（安装“错误 5：拒绝访问”等问题）。</summary>
public sealed class PermDoctorToolModule : ToolModule
{
    /// <summary>工具显示名（统一日志来源标记）。</summary>
    public const string ToolTitle = "权限医生";

    public override string Id => "permdoctor";
    public override string Title => ToolTitle;
    public override string Glyph => "🔐";
    public override string Description =>
        "列出文件夹的权限明细（ACL）、计算当前用户的有效权限并实测写入；发现孤儿 SID、断继承、缺权限等问题，一键修复或复制 icacls 等效命令。";

    public override UserControl CreateView()
    {
        var vm = new PermDoctorViewModel(Host, new DiagnoseService(new AclService()));
        return new PermDoctorView { DataContext = vm };
    }
}
