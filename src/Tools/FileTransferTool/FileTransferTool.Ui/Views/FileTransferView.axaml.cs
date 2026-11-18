using Avalonia.Controls;
using Avalonia.Interactivity;
using FileTransferTool.Ui.ViewModels;

namespace FileTransferTool.Ui.Views;

/// <summary>文件传输工具视图：Owner 注入（剪贴板/弹窗）+ 浏览列表双击进入目录 + 视图脱离退订。</summary>
public partial class FileTransferView : UserControl
{
    private FileTransferViewModel? _vm;

    public FileTransferView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BindVm();
        Loaded += (_, _) => BindVm();
        DetachedFromVisualTree += (_, _) => _vm?.Detach();
    }

    private void BindVm()
    {
        if (DataContext is not FileTransferViewModel vm) return;
        _vm = vm;
        vm.Owner = TopLevel.GetTopLevel(this) as Window;
    }

    /// <summary>浏览列表双击：选中的是目录则进入。</summary>
    private void BrowseGrid_DoubleTapped(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm?.BrowseSel is { IsDir: true })
            _vm.OpenBrowseItemCommand.Execute(null);
    }
}
