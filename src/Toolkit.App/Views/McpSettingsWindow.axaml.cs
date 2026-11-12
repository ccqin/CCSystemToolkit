using Avalonia.Controls;
using Toolkit.App.ViewModels;

namespace Toolkit.App.Views;

/// <summary>「MCP 服务」设置弹窗：服务启停/端口 + 接入授权（令牌）管理。</summary>
public partial class McpSettingsWindow : Window
{
    public McpSettingsWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            if (DataContext is McpSettingsViewModel vm) vm.Owner = this;
        };
        Closed += (_, _) => (DataContext as McpSettingsViewModel)?.Cleanup();
    }
}
