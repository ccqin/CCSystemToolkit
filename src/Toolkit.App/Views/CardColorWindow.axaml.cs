using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Toolkit.App.Views;

/// <summary>卡片自定义背景色对话框：内置 ColorPicker，ShowDialog 返回所选颜色（取消返回 null）。</summary>
public partial class CardColorWindow : Window
{
    public CardColorWindow()
    {
        InitializeComponent();
    }

    public CardColorWindow(Color initial) : this()
    {
        Picker.Color = initial;
    }

    /// <summary>便捷入口：以主窗口为 Owner 弹出取色框。</summary>
    public static async Task<Color?> PickAsync(Color? initial)
    {
        if (AppDialogs.Owner is not { } owner) return null;
        var win = new CardColorWindow(initial ?? Color.Parse("#5C6BC0"));
        return await win.ShowDialog<Color?>(owner);
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(Picker.Color);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
