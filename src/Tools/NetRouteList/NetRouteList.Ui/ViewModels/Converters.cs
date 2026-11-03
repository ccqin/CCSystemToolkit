using System.Globalization;
using Avalonia.Data.Converters;

namespace NetRouteList.Ui.ViewModels;

public static class Converters
{
    /// <summary>对象非空 → true（用于面板显隐）。</summary>
    public static readonly IValueConverter NotNullToBool =
        new FuncValueConverter<object?, bool>(o => o is not null);

    /// <summary>对象为空 → true。</summary>
    public static readonly IValueConverter NullToBool =
        new FuncValueConverter<object?, bool>(o => o is null);

    /// <summary>bool 反转。</summary>
    public static readonly IValueConverter NotBool =
        new FuncValueConverter<bool, bool>(b => !b);

    /// <summary>非空非白字符串 → true。</summary>
    public static readonly IValueConverter TextToBool =
        new FuncValueConverter<string?, bool>(s => !string.IsNullOrWhiteSpace(s));

    public static readonly IValueConverter CountToText =
        new FuncValueConverter<int, string>(c => c <= 0 ? "（空）" : $"{c} 条");

    /// <summary>true → 警示红 / false → 正常绿。</summary>
    public static readonly IValueConverter BoolToWarn =
        new FuncValueConverter<bool, Avalonia.Media.IBrush>(b =>
            Avalonia.Media.Brush.Parse(b ? "#D13438" : "#107C10"));

    /// <summary>true → 1（完全不透明）/ false → 0.5（弱化显示）。</summary>
    public static readonly IValueConverter BoolToDouble =
        new FuncValueConverter<bool, double>(b => b ? 1.0 : 0.5);
}

public sealed class BoolToStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is string p && p.Contains('|') && value is bool b)
        {
            var parts = p.Split('|');
            return b ? parts[0] : parts[1];
        }
        return value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
