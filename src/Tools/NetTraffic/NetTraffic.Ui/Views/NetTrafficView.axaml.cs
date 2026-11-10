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
}
