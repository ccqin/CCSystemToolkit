using Avalonia.Controls;
using Avalonia.Interactivity;
using NetRouteList.Ui.ViewModels;

namespace NetRouteList.Ui.Views;

public partial class RouteEditWindow : Window
{
    public RouteEditViewModel Vm => (RouteEditViewModel)DataContext!;

    public RouteMutationResult? Result { get; private set; }

    public RouteEditWindow()
    {
        InitializeComponent();
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var mutation = Vm.BuildMutation();
        if (mutation is null) return; // 校验失败信息已显示在 Vm.ErrorText

        Result = new RouteMutationResult
        {
            Mutation = mutation,
            Original = Vm.Original?.Entry,
        };
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}

public sealed class RouteMutationResult
{
    public required Core.Models.RouteMutation Mutation { get; init; }
    /// <summary>编辑模式：被修改的原条目；新增模式为 null。</summary>
    public Core.Models.RouteEntry? Original { get; init; }
}
