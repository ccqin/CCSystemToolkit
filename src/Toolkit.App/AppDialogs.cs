using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Toolkit.App;

/// <summary>宿主侧对话框辅助：取当前主窗口作为 ShowDialog 的 Owner（单窗口宿主下成立）。</summary>
public static class AppDialogs
{
    public static Window? Owner =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
