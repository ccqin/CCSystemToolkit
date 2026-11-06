using Avalonia.Controls;
using Avalonia.Interactivity;
using PlcScanner.Core.Models;
using PlcScanner.Ui.ViewModels;

namespace PlcScanner.Ui.Views;

public partial class PlcScannerView : UserControl
{
    public PlcScannerView()
    {
        InitializeComponent();
    }

    private async void OnDeviceDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is PlcScannerViewModel vm &&
            (sender as DataGrid)?.SelectedItem is DeviceItem item)
        {
            await vm.CopyIpAsync(item);
        }
    }
}
