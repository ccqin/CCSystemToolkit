using Avalonia.Controls;
using NetRouteList.Core.Services;
using NetRouteList.Ui.ViewModels;
using NetRouteList.Ui.Views;
using Toolkit.Shell;

namespace NetRouteList.Ui;

/// <summary>路由管理工具模块：把 NetRouteList 接入系统管理工具集。</summary>
public sealed class NetRouteListToolModule : ToolModule
{
    /// <summary>工具显示名（同时作为统一日志的来源标记，VM 上报日志时引用）。</summary>
    public const string ToolTitle = "路由管理";

    public override string Id => "netroutelist";
    public override string Title => ToolTitle;
    public override string Glyph => "🌐";
    public override string Description =>
        "查看、筛选、增删改 Windows 路由表；一键备份与恢复；自动诊断多默认路由、缺失直连路由、代理接管等常见网络问题。";

    public override UserControl CreateView()
    {
        var routes = new RouteService();
        var adapters = new AdapterService();
        var backups = new BackupService(routes, adapters);
        var diagnostics = new DiagnosticsService(routes);
        var vm = new NetRouteListViewModel(Host, routes, adapters, backups, diagnostics);
        return new NetRouteListView { DataContext = vm };
    }
}
