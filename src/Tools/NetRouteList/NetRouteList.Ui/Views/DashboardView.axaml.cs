using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using NetRouteList.Ui.ViewModels;

namespace NetRouteList.Ui.Views;

public partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();

    /// <summary>“⧉ 复制此问题”：把整条诊断（标题/明细/说明/建议）复制到剪贴板。</summary>
    private async void OnCopyIssue(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: DiagnosticIssueViewModel vm }) return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clip) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[{vm.SeverityText}] {vm.Title}");
        foreach (var line in vm.Affected)
            sb.AppendLine("  · " + line);
        sb.AppendLine();
        sb.AppendLine("说明：" + vm.Explanation);
        sb.AppendLine("建议：" + vm.Suggestion);
        await clip.SetTextAsync(sb.ToString());

        if (sender is Button btn)
        {
            btn.Content = "✔ 已复制";
            await Task.Delay(1200);
            btn.Content = "⧉ 复制此问题";
        }
    }
}
