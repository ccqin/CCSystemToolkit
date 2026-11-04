using Avalonia.Controls;
using Avalonia.Interactivity;
using ShellExt.Ui.ViewModels;

namespace ShellExt.Ui.Views;

public partial class ShellExtView : UserControl
{
    public ShellExtView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ShellExtViewModel vm)
        {
            vm.Owner = TopLevel.GetTopLevel(this) as Window;
        }
    }
}
