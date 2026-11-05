using Avalonia.Controls;
using Avalonia.Interactivity;
using StartupMgr.Ui.ViewModels;

namespace StartupMgr.Ui.Views;

public partial class StartupMgrView : UserControl
{
    public StartupMgrView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is StartupMgrViewModel vm)
        {
            vm.Owner = TopLevel.GetTopLevel(this) as Window;
        }
    }
}
