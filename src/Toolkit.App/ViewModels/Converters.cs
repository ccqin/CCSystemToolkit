using Avalonia.Data.Converters;

namespace Toolkit.App.ViewModels;

/// <summary>宿主 XAML 用的小转换器（与 NetRouteList.Ui 的 Converters 同一模式：x:Static 直接引用）。</summary>
public static class Converters
{
    /// <summary>集合数量 → 是否为空（用于日志空态提示）。</summary>
    public static readonly FuncValueConverter<int, bool> CountIsZero =
        new(c => c == 0);
}
