using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using PermDoctor.Core.Models;

namespace PermDoctor.Ui.ViewModels;

/// <summary>权限医生 XAML 用的小转换器。</summary>
public static class Converters
{
    /// <summary>写入实测结果 → 状态色（绿/红/橙）。</summary>
    public static readonly FuncValueConverter<WriteTestResult, IBrush> WriteTestBrush =
        new(r => r switch
        {
            WriteTestResult.Ok => Brushes.Green,
            WriteTestResult.CreateDenied or WriteTestResult.CleanupDenied => Brushes.OrangeRed,
            _ => Brushes.Gray,
        });

    /// <summary>bool → 启用/已断开（继承状态）。</summary>
    public static readonly FuncValueConverter<bool, string> BoolToOnOff =
        new(v => v ? "启用" : "已断开");

    /// <summary>规则是否继承 → 继承/显式。</summary>
    public static readonly FuncValueConverter<bool, string> AclSource =
        new(v => v ? "继承" : "显式");
}
