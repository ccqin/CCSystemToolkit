using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using MenuClean.Ui.ViewModels;

namespace MenuClean.Ui.Views;

public partial class MenuCleanView : UserControl
{
    public MenuCleanView()
    {
        InitializeComponent();
    }

    /// <summary>点击模拟菜单行 → 右侧显示该项详情。</summary>
    private void OnMenuRowTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: EntryItemViewModel vm } && DataContext is MenuCleanViewModel mvm)
        {
            mvm.SelectedEntry = vm;
        }
    }
}
