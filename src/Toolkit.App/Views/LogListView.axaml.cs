using Avalonia.Controls;

namespace Toolkit.App.Views;

/// <summary>统一日志列表（主页日志面板与状态栏 Flyout 共用，DataContext 为 MainViewModel）。</summary>
public partial class LogListView : UserControl
{
    public LogListView()
    {
        InitializeComponent();
    }
}
