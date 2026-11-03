using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;

namespace NetRouteList.Ui.Views;

/// <summary>代码构建的通用对话框：确认、结果报告、创建备份输入。</summary>
public static class Dialogs
{
    public static Window Owner { get; set; } = null!;

    private static Window BaseWindow(string title, double width, double height)
    {
        return new Window
        {
            Title = title,
            Width = width,
            Height = height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.Manual,
        };
    }

    private static Button Button(string text, bool accent = false)
    {
        var b = new Button { Content = text, Width = 110, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(0, 7) };
        if (accent) b.Classes.Add("accent");
        return b;
    }

    /// <summary>确认对话框。返回 true=用户点了“确定”。</summary>
    public static async Task<bool> Confirm(string title, string message, IReadOnlyList<string>? detailLines = null, string okText = "确定")
    {
        var ok = Button(okText, accent: true);
        var cancel = Button("取消");
        var win = BaseWindow(title, 620, 420);
        var msg = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(msg);
        if (detailLines is { Count: > 0 })
        {
            panel.Children.Add(new Border
            {
                BorderBrush = Brush.Parse("#666666"),
                BorderThickness = new Thickness(0.5),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                MaxHeight = 200,
                Child = new ScrollViewer
                {
                    Content = new StackPanel
                    {
                        Children =
                        {
                            new ItemsControl
                            {
                                ItemsSource = detailLines,
                                ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((s, _) =>
                                    new SelectableTextBlock
                                    {
                                        Text = s,
                                        TextWrapping = TextWrapping.Wrap,
                                        FontFamily = new FontFamily("Cascadia Mono,Consolas"),
                                        FontSize = 12,
                                        Margin = new Thickness(0, 2),
                                    }),
                            },
                        },
                    },
                },
            });
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 16, 0, 0),
            Children = { cancel, ok },
        };
        panel.Children.Add(buttons);
        win.Content = panel;

        bool result = false;
        ok.Click += (_, _) => { result = true; win.Close(); };
        cancel.Click += (_, _) => win.Close();
        await win.ShowDialog(Owner);
        return result;
    }

    /// <summary>
    /// 带复选框的确认对话框（例如“持久路由”选项）。
    /// 预览命令列表会随勾选状态实时刷新。返回 (是否确认, 勾选状态)。
    /// </summary>
    public static async Task<(bool Confirmed, bool Checked)> ConfirmWithCheck(
        string title, string message, string checkText, bool checkDefault,
        Func<bool, IReadOnlyList<string>> previewFactory, string okText = "确定")
    {
        var ok = Button(okText, accent: true);
        var cancel = Button("取消");
        var win = BaseWindow(title, 640, 480);
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });

        var check = new CheckBox
        {
            Content = checkText,
            IsChecked = checkDefault,
            Margin = new Thickness(0, 0, 0, 10),
        };
        panel.Children.Add(check);

        var list = new ItemsControl
        {
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((s, _) =>
                new TextBlock
                {
                    Text = s,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("Cascadia Mono,Consolas"),
                    FontSize = 12,
                    Margin = new Thickness(0, 2),
                }),
        };
        void Refresh() => list.ItemsSource = previewFactory(check.IsChecked == true);
        Refresh();
        check.IsCheckedChanged += (_, _) => Refresh();

        panel.Children.Add(new Border
        {
            BorderBrush = Brush.Parse("#666666"),
            BorderThickness = new Thickness(0.5),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            MaxHeight = 240,
            Child = new ScrollViewer { Content = list },
        });

        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 16, 0, 0),
            Children = { cancel, ok },
        });
        win.Content = panel;

        (bool, bool) result = (false, checkDefault);
        ok.Click += (_, _) => { result = (true, check.IsChecked == true); win.Close(); };
        cancel.Click += (_, _) => win.Close();
        await win.ShowDialog(Owner);
        return result;
    }

    /// <summary>结果报告窗口（只读，展示行列表）。</summary>
    public static async Task Report(string title, string message, IReadOnlyList<string> lines)
    {
        var ok = Button("关闭", accent: true);
        var win = BaseWindow(title, 780, 560);
        var copyAll = string.Join("\n", lines);

        var panel = new DockPanel { Margin = new Thickness(24) };
        var header = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };

        var list = new ListBox
        {
            ItemsSource = lines,
            FontFamily = new FontFamily("Cascadia Mono,Consolas"),
            FontSize = 12,
        };
        var copy = Button("复制全部", false);
        copy.Click += async (_, _) =>
        {
            if (win.Clipboard is { } clip) await clip.SetTextAsync(copyAll);
            copy.Content = "已复制";
        };

        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 14, 0, 0),
            Children = { copy, ok },
        };

        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        panel.Children.Add(bottom);
        panel.Children.Add(header);
        panel.Children.Add(list);
        win.Content = panel;

        ok.Click += (_, _) => win.Close();
        await win.ShowDialog(Owner);
    }

    /// <summary>创建备份对话框：输入名称与备注。</summary>
    public static async Task<(string name, string note)?> CreateBackup()
    {
        var win = BaseWindow("创建备份", 560, 320);
        var nameBox = new TextBox { Watermark = $"备份名称（默认：备份 {DateTime.Now:yyyy-MM-dd HH:mm:ss}）", Margin = new Thickness(0, 4, 0, 12) };
        var noteBox = new TextBox { Watermark = "备注（可选）：例如“修复 on-link 之前”“关闭 FlClash 后”…", AcceptsReturn = true, Height = 74, TextWrapping = TextWrapping.Wrap };

        var ok = Button("创建", accent: true);
        var cancel = Button("取消");

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "备份会完整保存当前 IPv4/IPv6 路由表与网卡快照（JSON 文件），不会改动系统。", TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "名称" });
        panel.Children.Add(nameBox);
        panel.Children.Add(new TextBlock { Text = "备注" });
        panel.Children.Add(noteBox);
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 16, 0, 0),
            Children = { cancel, ok },
        });
        win.Content = panel;

        (string, string)? result = null;
        ok.Click += (_, _) => { result = (nameBox.Text ?? "", noteBox.Text ?? ""); win.Close(); };
        cancel.Click += (_, _) => win.Close();
        await win.ShowDialog(Owner);
        return result;
    }
}
