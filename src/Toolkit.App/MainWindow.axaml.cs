using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Toolkit.App;

public partial class MainWindow : Window
{
    // 标题栏手动拖拽状态（BeginMoveDrag 在 WindowDecorations=BorderOnly 下实测不生效，改纯托管实现）
    private bool _titleDrag;
    private PixelPoint _dragStartPos;
    private PixelPoint _dragStartScreen;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 自绘标题栏：左键空白处拖拽移动窗口（指针捕获+屏幕坐标增量），双击切换最大化/还原。
    /// 落在按钮（返回/MCP/窗口控制）上的按下不处理，避免与点击冲突。
    /// </summary>
    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is Visual v && v.FindAncestorOfType<Button>() != null) return;

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        if (WindowState != WindowState.Normal || sender is not Control bar) return;
        _titleDrag = true;
        _dragStartPos = Position;
        _dragStartScreen = Avalonia.VisualExtensions.PointToScreen(this, e.GetPosition(this));
        e.Pointer.Capture(bar);
    }

    private void TitleBar_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_titleDrag) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            EndTitleDrag(e);
            return;
        }
        var cur = Avalonia.VisualExtensions.PointToScreen(this, e.GetPosition(this));
        Position = new PixelPoint(
            _dragStartPos.X + cur.X - _dragStartScreen.X,
            _dragStartPos.Y + cur.Y - _dragStartScreen.Y);
    }

    private void TitleBar_PointerReleased(object? sender, PointerReleasedEventArgs e) => EndTitleDrag(e);

    private void EndTitleDrag(PointerEventArgs e)
    {
        _titleDrag = false;
        e.Pointer.Capture(null);
    }

    private void MinimizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaxRestoreClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // 最大化时把按钮字形换成"还原"，还原时换回"最大化"
        if (change.Property == WindowStateProperty && MaxRestoreGlyph != null)
            MaxRestoreGlyph.Text = WindowState == WindowState.Maximized ? "❐" : "▢";
    }
}
