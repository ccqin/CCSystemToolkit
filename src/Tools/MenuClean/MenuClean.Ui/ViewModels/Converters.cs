using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MenuClean.Ui.ViewModels;

/// <summary>清理右键菜单 XAML 用的小转换器。</summary>
public static class Converters
{
    /// <summary>状态文本 → 颜色（已禁用=灰，其余=绿）。</summary>
    public static readonly FuncValueConverter<string, IBrush> StatusBrush =
        new(s => s?.StartsWith("已禁用") == true ? Brushes.Gray : Brushes.Green);
}
