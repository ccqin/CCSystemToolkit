using Avalonia.Controls;
using Avalonia.Interactivity;
using NetTraffic.Ui.ViewModels;

namespace NetTraffic.Ui.Views;

public partial class NetTrafficView : UserControl
{
    public NetTrafficView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NetTrafficViewModel vm)
        {
            vm.Owner = TopLevel.GetTopLevel(this) as Window;
        }
    }

        /// <summary>历史表双击行：直接打开该进程的流量曲线（与右键菜单等效）。</summary>
        private void HistoryGrid_DoubleTapped(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is ViewModels.NetTrafficViewModel vm && vm.SelectedHistoryRow is { } row)
                vm.ShowTrafficChartCommand.Execute(null);
        }
}
