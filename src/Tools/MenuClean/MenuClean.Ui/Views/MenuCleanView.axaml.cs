using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MenuClean.Ui.ViewModels;

namespace MenuClean.Ui.Views;

public partial class MenuCleanView : UserControl
{
    public MenuCleanView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MenuCleanViewModel vm)
        {
            vm.Owner = TopLevel.GetTopLevel(this) as Window;
        }
    }

    /// <summary>点击场景视图菜单行 / 来源视图注入行 → 右侧共享详情面板显示该项。</summary>
    private void OnMenuRowTapped(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: EntryItemViewModel vm } && DataContext is MenuCleanViewModel mvm)
        {
            mvm.SelectedEntry = vm;
        }
    }
}
