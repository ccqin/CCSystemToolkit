using Avalonia.Data.Converters;

namespace NetTraffic.Ui.ViewModels;

/// <summary>流量监控 XAML 用的小转换器。</summary>
public static class Converters
{
    public static readonly FuncValueConverter<bool, string> RunningText =
        new(r => r ? "■ 停止监控" : "▶ 开始监控");

    public static readonly FuncValueConverter<string?, string> NullToDash =
        new(v => string.IsNullOrWhiteSpace(v) ? "—" : v);

    public static readonly FuncValueConverter<DateTime?, string> StartTimeText =
        new(v => v?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—（需更高权限）");
}
