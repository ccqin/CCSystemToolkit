using Avalonia.Controls;
using Avalonia.Interactivity;
using SerialMcpTool.Ui.ViewModels;

namespace SerialMcpTool.Ui.Views;

/// <summary>串口 MCP 工具视图：Owner 注入（剪贴板）+ 新条目自动滚动 + 视图脱离退订。</summary>
public partial class SerialMcpView : UserControl
{
    private SerialMcpViewModel? _vm;

    public SerialMcpView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BindVm();
        Loaded += (_, _) => BindVm();
        DetachedFromVisualTree += (_, _) => _vm?.Detach();
    }

    private void BindVm()
    {
        if (DataContext is not SerialMcpViewModel vm) return;
        _vm = vm;
        // Loaded 前 TopLevel 尚未挂树，两处都绑（幂等）
        vm.Owner = TopLevel.GetTopLevel(this) as Window;
        vm.ItemAdded -= OnItemAdded;
        vm.ItemAdded += OnItemAdded;
    }

    private void OnItemAdded(SerialItemViewModel item) => LogList.ScrollIntoView(item);
}
