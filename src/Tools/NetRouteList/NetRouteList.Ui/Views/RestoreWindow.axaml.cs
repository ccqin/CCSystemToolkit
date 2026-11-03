using Avalonia.Controls;
using Avalonia.Interactivity;
using NetRouteList.Ui.ViewModels;

namespace NetRouteList.Ui.Views;

public partial class RestoreWindow : Window
{
    public RestoreWindowViewModel Vm => (RestoreWindowViewModel)DataContext!;

    public Core.Services.RestoreOptions? Options { get; private set; }

    public RestoreWindow()
    {
        InitializeComponent();
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        Options = Vm.BuildOptions();
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
