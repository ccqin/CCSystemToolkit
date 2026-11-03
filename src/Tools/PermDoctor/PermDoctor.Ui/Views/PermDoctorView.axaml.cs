using Avalonia.Controls;
using Avalonia.Interactivity;
using PermDoctor.Ui.ViewModels;

namespace PermDoctor.Ui.Views;

public partial class PermDoctorView : UserControl
{
    public PermDoctorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    // 注入 Owner（浏览/剪贴板需要主窗口），单窗口宿主下取 TopLevel 即可
    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PermDoctorViewModel vm)
        {
            vm.Owner = TopLevel.GetTopLevel(this) as Window;
        }
    }
}
